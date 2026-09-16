using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    /// <summary>
    /// The activation input policies. The router owns the edges; these drive it
    /// through the same press/release pair a game with its own input reader
    /// would use, with the clock supplied by the test because EditMode has no
    /// advancing <c>Time.time</c>.
    /// </summary>
    public sealed class AbilityInputPolicyTests
    {
        private readonly List<Object> owned = new();
        private float now;

        [SetUp]
        public void SetUp()
        {
            now = 0f;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in owned)
            {
                if (o != null)
                {
                    Object.DestroyImmediate(o);
                }
            }

            owned.Clear();
        }

        [Test]
        public void OnPress_ActivatesTheMomentTheInputGoesDown()
        {
            AbilityInputRouter router = NewRouter(out AbilitySystem system, out _);
            TimelineAbilityDefinition ability = NewAbility("test.input.press");
            Grant(system, ability);

            Assert.IsTrue(router.NotifyInputPressed(ability));
            Assert.AreEqual(ability, system.ActiveAbility);
        }

        [Test]
        public void OnHold_WaitsForTheHoldDurationAndThenActivatesOnItsOwn()
        {
            AbilityInputRouter router = NewRouter(out AbilitySystem system, out _);
            TimelineAbilityDefinition ability = NewAbility("test.input.hold");
            ability.SetActivationInputPolicyForTests(
                AbilityActivationInputPolicy.OnHold, 0.5f);
            Grant(system, ability);

            Assert.IsFalse(router.NotifyInputPressed(ability), "the press alone does nothing");
            Assert.IsFalse(system.IsActive);

            now = 0.4f;
            router.TickHeldInputs();
            Assert.IsFalse(system.IsActive, "still short of the hold duration");

            now = 0.5f;
            router.TickHeldInputs();
            Assert.AreEqual(ability, system.ActiveAbility, "the hold elapsed");
        }

        [Test]
        public void OnHold_ReleasingEarlyActivatesNothing()
        {
            AbilityInputRouter router = NewRouter(out AbilitySystem system, out _);
            TimelineAbilityDefinition ability = NewAbility("test.input.hold.aborted");
            ability.SetActivationInputPolicyForTests(
                AbilityActivationInputPolicy.OnHold, 0.5f);
            Grant(system, ability);

            router.NotifyInputPressed(ability);
            now = 0.2f;
            Assert.IsFalse(router.NotifyInputReleased(ability));

            now = 5f;
            router.TickHeldInputs();
            Assert.IsFalse(system.IsActive, "the abandoned hold left nothing behind");
        }

        [Test]
        public void OnRelease_ActivatesWhenTheInputComesBackUp()
        {
            AbilityInputRouter router = NewRouter(out AbilitySystem system, out _);
            TimelineAbilityDefinition ability = NewAbility("test.input.release");
            ability.SetActivationInputPolicyForTests(
                AbilityActivationInputPolicy.OnRelease, 0.2f);
            Grant(system, ability);

            Assert.IsFalse(router.NotifyInputPressed(ability));

            now = 0.3f;
            router.TickHeldInputs();
            Assert.IsFalse(system.IsActive, "On Release never activates while held");

            Assert.IsTrue(router.NotifyInputReleased(ability));
            Assert.AreEqual(ability, system.ActiveAbility);
        }

        [Test]
        public void OnRelease_IgnoresAPressShorterThanTheHoldDuration()
        {
            AbilityInputRouter router = NewRouter(out AbilitySystem system, out _);
            TimelineAbilityDefinition ability = NewAbility("test.input.release.tap");
            ability.SetActivationInputPolicyForTests(
                AbilityActivationInputPolicy.OnRelease, 0.5f);
            Grant(system, ability);

            router.NotifyInputPressed(ability);
            now = 0.1f;

            Assert.IsFalse(router.NotifyInputReleased(ability));
            Assert.IsFalse(system.IsActive);
        }

        [Test]
        public void AReleaseWithoutAPressDoesNothing()
        {
            AbilityInputRouter router = NewRouter(out AbilitySystem system, out _);
            TimelineAbilityDefinition ability = NewAbility("test.input.release.orphan");
            ability.SetActivationInputPolicyForTests(
                AbilityActivationInputPolicy.OnRelease, 0f);
            Grant(system, ability);

            Assert.IsFalse(router.NotifyInputReleased(ability));
            Assert.IsFalse(system.IsActive);
        }

        // ------------------------------------------------------------ helpers

        private AbilityInputRouter NewRouter(
            out AbilitySystem system, out GameObject owner)
        {
            owner = new GameObject("RouterOwner");
            owned.Add(owner);
            system = owner.AddComponent<AbilitySystem>();
            AbilityInputRouter router = owner.AddComponent<AbilityInputRouter>();
            // Awake timing in EditMode is not guaranteed; wire explicitly.
            SetField(router, "abilitySystem", system);
            router.TimeSource = () => now;
            return router;
        }

        private TimelineAbilityDefinition NewAbility(string abilityId)
        {
            TimelineAbilityDefinition ability = ScriptableObject.CreateInstance<TimelineAbilityDefinition>();
            owned.Add(ability);
            ability.SetAbilityIdForTests(abilityId);
            SetField(ability, "requiresTarget", false);

            var step = new AbilityStep();
            step.ConfigureForTests(1, 2, 10, 60f);
            ability.SetStepsForTests(step);
            return ability;
        }

        private void Grant(AbilitySystem system, params AbilityDefinition[] abilities)
        {
            AbilityLoadout loadout = ScriptableObject.CreateInstance<AbilityLoadout>();
            owned.Add(loadout);
            SetField(loadout, "abilities", abilities);
            SetField(system, "loadout", loadout);
        }

        private static void SetField<TValue>(object target, string fieldName, TValue value)
        {
            // Walks up the hierarchy: a private field of a base class is
            // invisible to a single GetField call, and an ability's own fields
            // now sit one level above the timeline type most fixtures use.
            for (System.Type type = target.GetType(); type != null; type = type.BaseType)
            {
                FieldInfo field = type.GetField(
                    fieldName,
                    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    field.SetValue(target, value);
                    return;
                }
            }

            Assert.Fail($"No field named {fieldName}.");
        }
    }
}
