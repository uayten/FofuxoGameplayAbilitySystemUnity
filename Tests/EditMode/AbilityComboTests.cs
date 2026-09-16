using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    /// <summary>
    /// A combo is one ability walking its steps, so these cover activation,
    /// the buffered-input window that carries it from one step to the next, and
    /// what happens when that input never arrives.
    /// </summary>
    public sealed class AbilityComboTests
    {
        private readonly List<Object> owned = new();

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
        public void CanActivate_ReturnsTrueWithoutStartingTheCombo()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition combo = NewCombo("test.combo", 2);
            Grant(system, combo);

            bool canActivate = system.CanActivate(
                combo, AbilityContext.FromTarget(owner, null), out string rejectionReason);

            Assert.IsTrue(canActivate, rejectionReason);
            Assert.IsFalse(system.IsActive);
        }

        [Test]
        public void CanActivate_ExplainsMissingLoadoutGrant()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition combo = NewCombo("test.combo.ungranted", 2);

            bool canActivate = system.CanActivate(
                combo, AbilityContext.FromTarget(owner, null), out string rejectionReason);

            Assert.IsFalse(canActivate);
            Assert.AreEqual("Ability is not granted by the current loadout.", rejectionReason);
        }

        [Test]
        public void QueuedInputBeforeContinuationFrameAdvancesWhenTheFrameArrives()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition combo = NewCombo("test.combo.manual", 2);
            combo.SetStepAdvancementForTests(AbilityStepAdvancement.Manual);
            combo.StepAt(0).ConfigureActionWindowsForTests(0, 2, 10);
            Grant(system, combo);

            Assert.IsTrue(system.TryActivate(combo, AbilityContext.FromTarget(owner, null)));
            Assert.AreEqual(0, system.ActiveStepIndex);
            Assert.IsTrue(system.TryQueueStepAdvance());

            system.Tick(1f / 60f);

            Assert.AreEqual(combo, system.ActiveAbility, "the ability itself never changes");
            Assert.AreEqual(1, system.ActiveStepIndex, "the buffered input advanced the step");
        }

        [Test]
        public void InputWindowExpiresWithoutCancellingTheCurrentStep()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition combo = NewCombo("test.combo.expire", 2);
            combo.SetStepAdvancementForTests(AbilityStepAdvancement.Manual);
            combo.StepAt(0).ConfigureActionWindowsForTests(0, 2, 3);
            Grant(system, combo);

            Assert.IsTrue(system.TryActivate(combo, AbilityContext.FromTarget(owner, null)));
            for (int i = 0; i < 6; i++)
            {
                system.Tick(1f / 60f);
            }

            Assert.AreEqual(combo, system.ActiveAbility, "the step keeps running");
            Assert.AreEqual(0, system.ActiveStepIndex, "and never advanced");
            Assert.IsFalse(
                system.TryQueueStepAdvance(),
                "the input window is closed once the step passes its input end frame");
        }

        [Test]
        public void WithoutInput_AManualComboEndsOnItsCurrentStep()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition combo = NewCombo("test.combo.noinput", 2);
            combo.SetStepAdvancementForTests(AbilityStepAdvancement.Manual);
            combo.StepAt(0).ConfigureActionWindowsForTests(0, 2, 4);
            Grant(system, combo);

            Assert.IsTrue(system.TryActivate(combo, AbilityContext.FromTarget(owner, null)));
            for (int i = 0; i < 40; i++)
            {
                system.Tick(1f / 60f);
            }

            Assert.IsFalse(system.IsActive, "the combo ended instead of waiting forever");
        }

        [Test]
        public void AnAutomaticComboChainsEveryStepOnItsOwn()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition combo = NewCombo("test.combo.auto", 3);
            combo.SetStepAdvancementForTests(AbilityStepAdvancement.Automatic);
            Grant(system, combo);

            Assert.IsTrue(system.TryActivate(combo, AbilityContext.FromTarget(owner, null)));

            int highestStep = 0;
            for (int i = 0; i < 120 && system.IsActive; i++)
            {
                system.Tick(1f / 60f);
                highestStep = Mathf.Max(highestStep, system.ActiveStepIndex);
            }

            Assert.AreEqual(2, highestStep, "every step ran");
            Assert.IsFalse(system.IsActive, "and the ability finished");
        }

        [Test]
        public void MovementUnlockFrameReleasesMovementBeforeTheStepCompletes()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition combo = NewCombo("test.combo.movement", 1);
            combo.StepAt(0).ConfigureActionWindowsForTests(3, 0, 0);
            Grant(system, combo);

            Assert.IsTrue(system.TryActivate(combo, AbilityContext.FromTarget(owner, null)));
            system.Tick(1f / 60f);
            Assert.IsTrue(system.IsMovementLocked, "still locked before the unlock frame");

            for (int i = 0; i < 4; i++)
            {
                system.Tick(1f / 60f);
            }

            Assert.IsFalse(system.IsMovementLocked, "unlocked while the step is still running");
            Assert.IsTrue(system.IsActive);
        }

        // ------------------------------------------------------------ helpers

        private AbilitySystem NewSystem(out GameObject owner)
        {
            owner = new GameObject("AbilitySystemOwner");
            owned.Add(owner);
            return owner.AddComponent<AbilitySystem>();
        }

        private TimelineAbilityDefinition NewCombo(string abilityId, int stepCount)
        {
            TimelineAbilityDefinition ability = ScriptableObject.CreateInstance<TimelineAbilityDefinition>();
            owned.Add(ability);
            ability.SetAbilityIdForTests(abilityId);
            SetField(ability, "requiresTarget", false);

            var steps = new AbilityStep[stepCount];
            for (int i = 0; i < stepCount; i++)
            {
                steps[i] = new AbilityStep();
                steps[i].ConfigureForTests(1, 2, 10, 60f);
            }

            ability.SetStepsForTests(steps);
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
