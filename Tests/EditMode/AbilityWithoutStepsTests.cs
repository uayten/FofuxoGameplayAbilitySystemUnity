using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    /// <summary>
    /// An ability with no steps: no timeline, no frames, no phases. It activates,
    /// runs whatever its subclass does, and stays active until something ends
    /// it — the shape for an ability whose animation is played elsewhere, or
    /// that has no animation at all.
    ///
    /// The rule these pin is the one that is easy to break by accident: nothing
    /// completes it on its own. A timeline is what supplies a last frame, and
    /// there is none.
    /// </summary>
    public sealed class AbilityWithoutStepsTests
    {
        private sealed class RecordingAbility : AbilityDefinition
        {
            public readonly List<string> Calls = new();

            protected internal override void OnActivated(AbilityInstance instance)
            {
                Calls.Add("activated");
            }

            protected internal override void OnCompleted(AbilityInstance instance)
            {
                Calls.Add("completed");
            }

            protected internal override void OnCancelled(
                AbilityInstance instance, GameplayTag cancelTag)
            {
                Calls.Add($"cancelled:{cancelTag}");
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

        /// <summary>
        /// Not "a timeline with nothing in it": the concept is absent from the
        /// type. An ability carries steps only by being a timeline ability.
        /// </summary>
        [Test]
        public void APlainAbilityCarriesNoTimeline()
        {
            AbilityDefinition ability = ScriptableObject.CreateInstance<AbilityDefinition>();
            owned.Add(ability);

            Assert.IsFalse(ability is TimelineAbilityDefinition);
            Assert.IsNull(new AbilityInstance(ability, default).Timeline);
            Assert.IsNull(new AbilityInstance(ability, default).Step);
        }

        [Test]
        public void AnAbilityWithoutStepsIsValid()
        {
            AbilityDefinition ability = ScriptableObject.CreateInstance<AbilityDefinition>();
            owned.Add(ability);
            ability.SetAbilityIdForTests("test.nosteps.valid");

            Assert.IsTrue(ability.TryValidate(out string error), error);
        }

        /// <summary>
        /// The base class runs no code of its own, so an asset of exactly that
        /// type with no timeline can never end. That is worth saying out loud in
        /// the Inspector, and it is a warning rather than an error because the
        /// asset is still legal — a consumer may end it from outside.
        /// </summary>
        [Test]
        public void TheBaseTypeWithoutStepsWarnsThatNothingEndsIt()
        {
            AbilityDefinition ability = ScriptableObject.CreateInstance<AbilityDefinition>();
            owned.Add(ability);
            ability.SetAbilityIdForTests("test.nosteps.warning");

            Assert.IsTrue(ability.TryGetAuthoringWarning(out string warning));
            StringAssert.Contains("nothing will", warning);
        }

        [Test]
        public void ASubclassWithoutStepsDoesNotWarn()
        {
            RecordingAbility ability = NewAbility("test.nosteps.subclass");

            Assert.IsFalse(ability.TryGetAuthoringWarning(out _));
        }

        /// <summary>
        /// There is no step hook to fire: <c>OnStepStarted</c> lives on the
        /// timeline type, which is the point — a plain ability cannot even
        /// declare an opinion about steps.
        /// </summary>
        [Test]
        public void ActivationReportsNoStepStart()
        {
            RecordingAbility ability = NewAbility("test.nosteps.activate");
            AbilitySystem system = NewSystem(ability, out GameObject owner);

            Assert.IsTrue(system.TryActivate(
                ability, AbilityContext.FromTarget(owner, null)));

            CollectionAssert.AreEqual(new[] { "activated" }, ability.Calls);
        }

        [Test]
        public void ItStaysActiveWhileNothingEndsIt()
        {
            RecordingAbility ability = NewAbility("test.nosteps.stays");
            AbilitySystem system = NewSystem(ability, out GameObject owner);
            system.TryActivate(ability, AbilityContext.FromTarget(owner, null));

            for (int i = 0; i < 600; i++)
            {
                system.Tick(1f / 60f);
            }

            Assert.IsTrue(system.IsActive);
            Assert.AreSame(ability, system.ActiveAbility);
            CollectionAssert.DoesNotContain(ability.Calls, "completed");
        }

        /// <summary>
        /// There is no frame to report, but there is a duration: how long the
        /// activation has been up is the only thing a debugger can say about it.
        /// </summary>
        [Test]
        public void ItsClockStillRuns()
        {
            RecordingAbility ability = NewAbility("test.nosteps.clock");
            AbilitySystem system = NewSystem(ability, out GameObject owner);
            system.TryActivate(ability, AbilityContext.FromTarget(owner, null));

            for (int i = 0; i < 30; i++)
            {
                system.Tick(1f / 60f);
            }

            Assert.AreEqual(0, system.ActiveFrame);
            Assert.AreEqual(0.5f, system.ActiveInstances[0].ElapsedTime, 0.001f);
        }

        [Test]
        public void ItCompletesWhenItIsTold()
        {
            RecordingAbility ability = NewAbility("test.nosteps.complete");
            AbilitySystem system = NewSystem(ability, out GameObject owner);
            system.TryActivate(ability, AbilityContext.FromTarget(owner, null));
            system.Tick(1f / 60f);

            Assert.IsTrue(system.TryCompleteActiveAbility(ability));
            Assert.IsFalse(system.IsActive);
            CollectionAssert.Contains(ability.Calls, "completed");
        }

        [Test]
        public void ItCancelsWhenItIsTold()
        {
            RecordingAbility ability = NewAbility("test.nosteps.cancel");
            AbilitySystem system = NewSystem(ability, out GameObject owner);
            system.TryActivate(ability, AbilityContext.FromTarget(owner, null));
            system.Tick(1f / 60f);

            Assert.IsTrue(system.TryCancelActiveAbility(CommonGameplayTags.CancelManual));
            Assert.IsFalse(system.IsActive);
            CollectionAssert.Contains(
                ability.Calls, $"cancelled:{CommonGameplayTags.CancelManual}");
        }

        /// <summary>
        /// The displacement-only ability: its work is a task, and the task
        /// running is the whole point of the activation staying up. Ending is
        /// still the caller's call, which is what this asserts after the task is
        /// long done.
        /// </summary>
        [Test]
        public void ItHostsTasksAndOutlivesThem()
        {
            RecordingAbility ability = NewAbility("test.nosteps.task");
            AbilitySystem system = NewSystem(ability, out GameObject owner);
            system.TryActivate(ability, AbilityContext.FromTarget(owner, null));

            WaitDelayTask wait = system.RunTask(new WaitDelayTask(0.25f));
            for (int i = 0; i < 30; i++)
            {
                system.Tick(1f / 60f);
            }

            Assert.AreEqual(AbilityTaskState.Succeeded, wait.State);
            Assert.IsTrue(system.IsActive);
        }

        /// <summary>
        /// With no step there is no unlock frame, so the ability-level flag is
        /// the whole rule and it holds for as long as the activation does.
        /// </summary>
        [Test]
        public void ItLocksMovementForAsLongAsItRuns()
        {
            RecordingAbility ability = NewAbility("test.nosteps.movement");
            SetField(ability, "lockMovementDuringAbility", true);
            AbilitySystem system = NewSystem(ability, out GameObject owner);
            system.TryActivate(ability, AbilityContext.FromTarget(owner, null));

            system.Tick(1f / 60f);
            Assert.IsTrue(system.IsMovementLocked);

            system.TryCompleteActiveAbility(ability);
            Assert.IsFalse(system.IsMovementLocked);
        }

        private RecordingAbility NewAbility(string id)
        {
            RecordingAbility ability = ScriptableObject.CreateInstance<RecordingAbility>();
            owned.Add(ability);
            ability.SetAbilityIdForTests(id);
            SetField(ability, "requiresTarget", false);
            return ability;
        }

        private AbilitySystem NewSystem(AbilityDefinition ability, out GameObject owner)
        {
            owner = new GameObject("SteplessOwner");
            owned.Add(owner);
            AbilityLoadout loadout = ScriptableObject.CreateInstance<AbilityLoadout>();
            owned.Add(loadout);
            SetField(loadout, "abilities", new[] { ability });
            AbilitySystem system = owner.AddComponent<AbilitySystem>();
            SetField(system, "loadout", loadout);
            return system;
        }

        /// <summary>
        /// Walks up the hierarchy: a derived ability's private base fields are
        /// invisible to a single GetField call, which is exactly the case here.
        /// </summary>
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
