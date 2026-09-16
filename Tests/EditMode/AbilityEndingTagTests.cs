using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    /// <summary>
    /// An ability ends as a completion or as a cancellation carrying a tag, and
    /// that tag is the whole outcome vocabulary a consumer has. These tests pin
    /// the three endings a handler must be able to tell apart: the game asked
    /// (<c>Cancel.Manual</c>), the consumer threw (<c>Cancel.Failed</c>), and
    /// the owner is going away (<c>Cancel.OwnerTeardown</c>). All three used to
    /// arrive as <c>Cancel.Manual</c>.
    /// </summary>
    public sealed class AbilityEndingTagTests
    {
        private const float Tick = 1f / 60f;

        /// <summary>
        /// A consumer effect that throws where a real one would call into game
        /// code — a damage receiver, a health rule, an event handler.
        /// </summary>
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

        [Test]
        public void AnExceptionWhileExecuting_EndsTheAbilityWithCancelFailed()
        {
            TimelineAbilityDefinition ability = NewAbility("test.ending.failed");
            AbilitySystem system = NewSystem(ability, out GameObject owner);
            GiveThrowingEffect(ability);

            GameplayTag cancelledWith = default;
            system.AbilityCancelled += (_, tag) => cancelledWith = tag;

            Assert.IsTrue(system.TryActivate(ability, AbilityContext.FromTarget(owner, null)));

            LogAssert.Expect(LogType.Exception, new Regex("consumer effect threw"));
            system.Tick(Tick);

            Assert.AreEqual(CommonGameplayTags.CancelFailed, cancelledWith);
            Assert.AreEqual(0, system.ActiveAbilityCount, "nothing was left half advanced");
        }

        [Test]
        public void AnExceptionWhileExecuting_IsNotReportedAsADeliberateCancel()
        {
            TimelineAbilityDefinition ability = NewAbility("test.ending.failed.notmanual");
            AbilitySystem system = NewSystem(ability, out GameObject owner);
            GiveThrowingEffect(ability);

            GameplayTag cancelledWith = default;
            system.AbilityCancelled += (_, tag) => cancelledWith = tag;

            Assert.IsTrue(system.TryActivate(ability, AbilityContext.FromTarget(owner, null)));
            LogAssert.Expect(LogType.Exception, new Regex("consumer effect threw"));
            system.Tick(Tick);

            Assert.AreNotEqual(
                CommonGameplayTags.CancelManual,
                cancelledWith,
                "a crash is not the game asking for a cancel");
        }

        [Test]
        public void DisablingTheOwnerMidActivation_EndsTheAbilityWithOwnerTeardown()
        {
            TimelineAbilityDefinition ability = NewAbility("test.ending.disable");
            AbilitySystem system = NewSystem(ability, out GameObject owner);

            GameplayTag cancelledWith = default;
            system.AbilityCancelled += (_, tag) => cancelledWith = tag;

            Assert.IsTrue(system.TryActivate(ability, AbilityContext.FromTarget(owner, null)));
            SendUnityMessage(system, "OnDisable");

            Assert.AreEqual(CommonGameplayTags.CancelOwnerTeardown, cancelledWith);
            Assert.AreEqual(0, system.ActiveAbilityCount);
        }

        [Test]
        public void DestroyingTheOwnerMidActivation_EndsTheAbilityWithOwnerTeardown()
        {
            TimelineAbilityDefinition ability = NewAbility("test.ending.destroy");
            AbilitySystem system = NewSystem(ability, out GameObject owner);

            GameplayTag cancelledWith = default;
            system.AbilityCancelled += (_, tag) => cancelledWith = tag;

            Assert.IsTrue(system.TryActivate(ability, AbilityContext.FromTarget(owner, null)));
            SendUnityMessage(system, "OnDestroy");

            Assert.AreEqual(CommonGameplayTags.CancelOwnerTeardown, cancelledWith);
            Assert.AreEqual(0, system.ActiveAbilityCount);
        }

        [Test]
        public void ADeliberateCancel_StillReportsCancelManual()
        {
            TimelineAbilityDefinition ability = NewAbility("test.ending.manual");
            AbilitySystem system = NewSystem(ability, out GameObject owner);

            GameplayTag cancelledWith = default;
            system.AbilityCancelled += (_, tag) => cancelledWith = tag;

            Assert.IsTrue(system.TryActivate(ability, AbilityContext.FromTarget(owner, null)));
            Assert.IsTrue(system.TryCancelActiveAbility(CommonGameplayTags.CancelManual));

            Assert.AreEqual(CommonGameplayTags.CancelManual, cancelledWith);
        }

        /// <summary>
        /// The package endings are distinct tags, so an ability that opts into
        /// <c>Only Listed Tags</c> can accept one without accepting the others.
        /// That is what a single shared tag made impossible.
        /// </summary>
        [Test]
        public void TheEndingTagsAreDistinctFromEachOther()
        {
            GameplayTag[] tags =
            {
                CommonGameplayTags.CancelManual,
                CommonGameplayTags.CancelTargetLost,
                CommonGameplayTags.CancelPhysicsForce,
                CommonGameplayTags.CancelStepTimeout,
                CommonGameplayTags.CancelSuperseded,
                CommonGameplayTags.CancelFailed,
                CommonGameplayTags.CancelOwnerTeardown,
            };

            CollectionAssert.AllItemsAreUnique(tags);
            foreach (GameplayTag tag in tags)
            {
                Assert.IsFalse(tag.IsEmpty);
                StringAssert.StartsWith("Cancel.", tag.Value);
            }
        }

        /// <summary>
        /// A teardown cancellation ignores the cancel policy: an uninterruptible
        /// ability still ends when its actor stops existing.
        /// </summary>
        [Test]
        public void OwnerTeardown_EndsEvenAnUninterruptibleAbility()
        {
            TimelineAbilityDefinition ability = NewAbility("test.ending.stubborn");
            ability.SetCancellationForTests(AbilityCancelPolicy.Nothing);
            AbilitySystem system = NewSystem(ability, out GameObject owner);

            GameplayTag cancelledWith = default;
            system.AbilityCancelled += (_, tag) => cancelledWith = tag;

            Assert.IsTrue(system.TryActivate(ability, AbilityContext.FromTarget(owner, null)));
            Assert.IsFalse(
                system.TryCancelActiveAbility(CommonGameplayTags.CancelManual),
                "the policy refuses an ordinary request");

            SendUnityMessage(system, "OnDisable");

            Assert.AreEqual(CommonGameplayTags.CancelOwnerTeardown, cancelledWith);
            Assert.AreEqual(0, system.ActiveAbilityCount);
        }

        // ------------------------------------------------------------ helpers

        /// <summary>
        /// Calls one of the Unity lifecycle messages directly. Edit Mode does
        /// not dispatch <c>OnDisable</c> or <c>OnDestroy</c> to a component
        /// without <c>[ExecuteAlways]</c>, so disabling or destroying the
        /// component here would run nothing at all. What is under test is what
        /// the handler does once Unity calls it, and this reaches exactly that.
        /// </summary>
        private static void SendUnityMessage(AbilitySystem system, string message)
        {
            MethodInfo method = typeof(AbilitySystem).GetMethod(
                message,
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, $"AbilitySystem has no {message} message.");
            method.Invoke(system, null);
        }

        private void GiveThrowingEffect(TimelineAbilityDefinition ability)
        {
            ThrowingEffectDefinition effect =
                ScriptableObject.CreateInstance<ThrowingEffectDefinition>();
            owned.Add(effect);
            effect.name = "GE_Debug_TestThrow";
            effect.ConfigureForTests(
                GameplayEffectDurationPolicy.Instant,
                GameplayEffectTargeting.Owner,
                System.Array.Empty<GameplayEffectModifier>());

            AbilityStep step = ability.FirstStepForTests;
            step.SetEffectTriggersForTests(new[] { new AbilityEffectTrigger(1, effect) });
        }

        private TimelineAbilityDefinition NewAbility(string abilityId)
        {
            TimelineAbilityDefinition ability = ScriptableObject.CreateInstance<TimelineAbilityDefinition>();
            owned.Add(ability);
            ability.SetAbilityIdForTests(abilityId);
            SetField(ability, "requiresTarget", false);
            AbilityStep step = new();
            step.ConfigureForTests(1, 2, 30, 60f);
            ability.SetStepsForTests(step);
            return ability;
        }

        private AbilitySystem NewSystem(AbilityDefinition ability, out GameObject owner)
        {
            owner = new GameObject("EndingTagOwner");
            owned.Add(owner);
            AbilityLoadout loadout = ScriptableObject.CreateInstance<AbilityLoadout>();
            owned.Add(loadout);
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
