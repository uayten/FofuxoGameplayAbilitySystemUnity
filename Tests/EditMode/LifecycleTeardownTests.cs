using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    /// <summary>
    /// What has to be true after an activation ends badly rather than well: the
    /// owner was disabled or destroyed under it, or a consumer threw in the
    /// middle of it.
    ///
    /// Both paths already ran cleanup and neither was covered, which is the
    /// dangerous shape for teardown code — it is only exercised when something
    /// has already gone wrong, so a regression in it is invisible until the day
    /// it matters. The invariant across every test here is the same: no
    /// half-started ability, no task outliving its activation, and no tag the
    /// owner keeps forever.
    /// </summary>
    public sealed class LifecycleTeardownTests
    {
        private const float Tick = 1f / 60f;
        private static readonly GameplayTag Attacking = new("State.Test.Attacking");

        /// <summary>Throws where a consumer's own code would run.</summary>
        private sealed class ThrowingEffectDefinition : GameplayEffectDefinition
        {
            protected override bool Execute(GameplayEffectSpec spec, bool periodic)
            {
                throw new System.InvalidOperationException("consumer effect threw");
            }
        }

        private readonly List<Object> owned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (Object item in owned)
            {
                if (item != null)
                {
                    Object.DestroyImmediate(item);
                }
            }

            owned.Clear();
        }

        // ------------------------------------------------------------ teardown

        [Test]
        public void DisablingTheOwner_DropsTheActivationsTaskScope()
        {
            TimelineAbilityDefinition ability = NewAbility("test.teardown.tasks");
            AbilitySystem system = NewSystem(ability, out GameObject owner);
            Assert.IsTrue(system.TryActivate(ability, AbilityContext.FromTarget(owner, null)));

            WaitDelayTask task = system.RunTask(new WaitDelayTask(10f));
            Assert.IsNotNull(task);
            Assert.IsTrue(task.IsRunning);

            SendUnityMessage(system, "OnDisable");

            Assert.IsTrue(task.IsFinished, "a task never outlives its activation");
            Assert.AreEqual(0, system.ActiveAbilityCount);
        }

        [Test]
        public void DisablingTheOwner_DropsActorTasksToo()
        {
            TimelineAbilityDefinition ability = NewAbility("test.teardown.actortasks");
            AbilitySystem system = NewSystem(ability, out GameObject owner);

            WaitDelayTask task = system.RunActorTask(new WaitDelayTask(10f));
            Assert.IsTrue(task.IsRunning);

            SendUnityMessage(system, "OnDisable");

            Assert.IsTrue(task.IsFinished, "the actor is going away, and so is its work");
            Assert.AreEqual(0, system.ActorTasks.Count);
        }

        [Test]
        public void DisablingTheOwner_ReleasesTheTagsTheAbilityGranted()
        {
            TimelineAbilityDefinition ability = NewAbility("test.teardown.tags");
            SetField(ability, "grantedTags", new[] { Attacking });
            AbilitySystem system = NewSystem(ability, out GameObject owner);

            Assert.IsTrue(system.TryActivate(ability, AbilityContext.FromTarget(owner, null)));
            Assert.IsTrue(system.HasTag(Attacking));

            SendUnityMessage(system, "OnDisable");

            Assert.IsFalse(system.HasTag(Attacking), "no tag the owner keeps forever");
        }

        [Test]
        public void DestroyingTheEffectContainer_LeavesNoActiveEffect()
        {
            GameObject actor = Own(new GameObject("EffectTeardownActor"));
            AttributeSet attributes = actor.AddComponent<AttributeSet>();
            attributes.SetInitialValues(new[]
            {
                new AttributeSet.InitialValue(new GameplayAttribute("Test.Speed"), 10f, 0f, 100f),
            });

            GameplayEffectDefinition buff =
                Own(ScriptableObject.CreateInstance<GameplayEffectDefinition>());
            buff.name = "GE_Buff_Teardown";
            buff.ConfigureForTests(
                GameplayEffectDurationPolicy.Infinite,
                GameplayEffectTargeting.Owner,
                new[]
                {
                    new GameplayEffectModifier(
                        new GameplayAttribute("Test.Speed"), AttributeOperation.Add, 5f),
                });

            GameplayEffectContainer container = GameplayEffectContainer.For(actor);
            GameplayEffectApplicationResult applied = container.Apply(
                new GameplayEffectSpec(buff, actor, actor, 1));
            Assert.IsTrue(applied.Succeeded);
            Assert.IsTrue(container.IsActive(applied.Handle));

            SendUnityMessage(container, "OnDestroy");

            Assert.IsFalse(container.IsActive(applied.Handle), "no active effect stranded");
            Assert.AreEqual(0, container.GetStackCount(applied.Handle));
        }

        [Test]
        public void DestroyingTheEffectContainer_ReleasesTheTagsItGranted()
        {
            TimelineAbilityDefinition ability = NewAbility("test.teardown.effecttags");
            AbilitySystem system = NewSystem(ability, out GameObject owner);

            GameplayTag slowed = new("State.Test.Slowed");
            GameplayEffectDefinition debuff =
                Own(ScriptableObject.CreateInstance<GameplayEffectDefinition>());
            debuff.name = "GE_Debuff_Teardown";
            debuff.ConfigureForTests(
                GameplayEffectDurationPolicy.Infinite,
                GameplayEffectTargeting.Owner,
                System.Array.Empty<GameplayEffectModifier>());
            debuff.SetTagsForTests(granted: new[] { slowed });

            GameplayEffectContainer container = GameplayEffectContainer.For(owner);
            Assert.IsTrue(container.Apply(new GameplayEffectSpec(debuff, owner, owner, 1))
                .Succeeded);
            Assert.IsTrue(system.HasTag(slowed));

            SendUnityMessage(container, "OnDestroy");

            Assert.IsFalse(system.HasTag(slowed));
        }

        // --------------------------------------------------- consumer failure

        [Test]
        public void AnEffectThatThrows_LeavesNoHalfStartedAbility()
        {
            TimelineAbilityDefinition ability = NewAbility("test.failure.effect");
            SetField(ability, "grantedTags", new[] { Attacking });
            AbilitySystem system = NewSystem(ability, out GameObject owner);
            GiveThrowingEffect(ability);

            Assert.IsTrue(system.TryActivate(ability, AbilityContext.FromTarget(owner, null)));
            WaitDelayTask task = system.RunTask(new WaitDelayTask(10f));

            LogAssert.Expect(LogType.Exception, new Regex("consumer effect threw"));
            system.Tick(Tick);

            Assert.AreEqual(0, system.ActiveAbilityCount, "no half-started ability");
            Assert.IsTrue(task.IsFinished, "no leaked task");
            Assert.IsFalse(system.HasTag(Attacking), "no tag kept forever");
        }

        /// <summary>
        /// A handler that throws on the way out must not be able to undo the
        /// teardown that already happened: the activation is torn down before
        /// the event is raised, and this is what pins that order.
        /// </summary>
        [Test]
        public void ACancelledHandlerThatThrows_StillLeavesTheOwnerClean()
        {
            TimelineAbilityDefinition ability = NewAbility("test.failure.cancelhandler");
            SetField(ability, "grantedTags", new[] { Attacking });
            AbilitySystem system = NewSystem(ability, out GameObject owner);
            system.AbilityCancelled += (_, _) =>
                throw new System.InvalidOperationException("cancel handler threw");

            Assert.IsTrue(system.TryActivate(ability, AbilityContext.FromTarget(owner, null)));

            Assert.Throws<System.InvalidOperationException>(
                () => system.TryCancelActiveAbility(CommonGameplayTags.CancelManual));

            Assert.AreEqual(0, system.ActiveAbilityCount);
            Assert.IsFalse(system.HasTag(Attacking));
        }

        [Test]
        public void ACompletedHandlerThatThrows_StillLeavesTheOwnerClean()
        {
            TimelineAbilityDefinition ability = NewAbility("test.failure.completehandler");
            SetField(ability, "grantedTags", new[] { Attacking });
            AbilitySystem system = NewSystem(ability, out GameObject owner);
            system.AbilityCompleted += _ =>
                throw new System.InvalidOperationException("complete handler threw");

            Assert.IsTrue(system.TryActivate(ability, AbilityContext.FromTarget(owner, null)));

            Assert.Throws<System.InvalidOperationException>(
                () => system.TryCompleteActiveAbility(ability));

            Assert.AreEqual(0, system.ActiveAbilityCount);
            Assert.IsFalse(system.HasTag(Attacking));
        }

        /// <summary>
        /// A cue is cosmetic, and a presenter that throws is the most likely
        /// consumer failure of the lot — it runs on the timeline, inside the
        /// tick, so the activation ends under <c>Cancel.Failed</c> rather than
        /// carrying on half advanced.
        /// </summary>
        [Test]
        public void ACueHandlerThatThrows_EndsTheActivationCleanly()
        {
            TimelineAbilityDefinition ability = NewAbility("test.failure.cue");
            SetField(ability, "grantedTags", new[] { Attacking });
            ability.FirstStepForTests.SetCueTriggersForTests(new[]
            {
                new GameplayCueTrigger(1, new GameplayTag("Cue.Test.Impact")),
            });

            AbilitySystem system = NewSystem(ability, out GameObject owner);
            system.GameplayCueTriggered += _ =>
                throw new System.InvalidOperationException("cue presenter threw");

            Assert.IsTrue(system.TryActivate(ability, AbilityContext.FromTarget(owner, null)));
            Assert.Throws<System.InvalidOperationException>(() => system.Tick(Tick));

            system.ForceCancelActiveAbility(CommonGameplayTags.CancelFailed);
            Assert.AreEqual(0, system.ActiveAbilityCount);
            Assert.IsFalse(system.HasTag(Attacking));
        }

        /// <summary>
        /// Teardown after a consumer failure has to be idempotent: the owner is
        /// disabled and destroyed in the same frame all the time.
        /// </summary>
        [Test]
        public void TearingDownTwiceIsHarmless()
        {
            TimelineAbilityDefinition ability = NewAbility("test.teardown.twice");
            SetField(ability, "grantedTags", new[] { Attacking });
            AbilitySystem system = NewSystem(ability, out GameObject owner);
            Assert.IsTrue(system.TryActivate(ability, AbilityContext.FromTarget(owner, null)));

            SendUnityMessage(system, "OnDisable");
            SendUnityMessage(system, "OnDestroy");

            Assert.AreEqual(0, system.ActiveAbilityCount);
            Assert.IsFalse(system.HasTag(Attacking));
        }

        // ------------------------------------------------------------ helpers

        /// <summary>
        /// Edit Mode does not dispatch <c>OnDisable</c> or <c>OnDestroy</c> to a
        /// component without <c>[ExecuteAlways]</c>, so the message is called
        /// directly. What is under test is what the handler does once Unity
        /// reaches it.
        /// </summary>
        private static void SendUnityMessage(MonoBehaviour behaviour, string message)
        {
            MethodInfo method = behaviour.GetType().GetMethod(
                message,
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, $"{behaviour.GetType().Name} has no {message} message.");
            method.Invoke(behaviour, null);
        }

        private void GiveThrowingEffect(TimelineAbilityDefinition ability)
        {
            ThrowingEffectDefinition effect =
                Own(ScriptableObject.CreateInstance<ThrowingEffectDefinition>());
            effect.name = "GE_Debug_TestThrow";
            effect.ConfigureForTests(
                GameplayEffectDurationPolicy.Instant,
                GameplayEffectTargeting.Owner,
                System.Array.Empty<GameplayEffectModifier>());
            ability.FirstStepForTests.SetEffectTriggersForTests(
                new[] { new AbilityEffectTrigger(1, effect) });
        }

        private T Own<T>(T item) where T : Object
        {
            owned.Add(item);
            return item;
        }

        private TimelineAbilityDefinition NewAbility(string abilityId)
        {
            TimelineAbilityDefinition ability = Own(ScriptableObject.CreateInstance<TimelineAbilityDefinition>());
            ability.SetAbilityIdForTests(abilityId);
            SetField(ability, "requiresTarget", false);
            AbilityStep step = new();
            step.ConfigureForTests(1, 2, 30, 60f);
            ability.SetStepsForTests(step);
            return ability;
        }

        private AbilitySystem NewSystem(AbilityDefinition ability, out GameObject owner)
        {
            owner = Own(new GameObject("TeardownOwner"));
            AbilityLoadout loadout = Own(ScriptableObject.CreateInstance<AbilityLoadout>());
            SetField(loadout, "abilities", new[] { ability });
            AbilitySystem system = owner.AddComponent<AbilitySystem>();
            SetField(system, "loadout", loadout);
            return system;
        }

        private static void SetField<TValue>(object target, string name, TValue value)
        {
            for (System.Type type = target.GetType(); type != null; type = type.BaseType)
            {
                FieldInfo field = type.GetField(
                    name,
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                {
                    field.SetValue(target, value);
                    return;
                }
            }

            Assert.Fail($"No field named {name}.");
        }
    }
}
