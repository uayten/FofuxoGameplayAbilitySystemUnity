using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    public sealed class AbilitySystemSequenceTests
    {
        [Test]
        public void CanActivateSequence_ReturnsTrueWithoutStartingTheSequence()
        {
            GameObject owner = new("AbilitySystemOwner");
            AbilityDefinition ability = ScriptableObject.CreateInstance<AbilityDefinition>();
            AbilitySequenceDefinition sequence =
                ScriptableObject.CreateInstance<AbilitySequenceDefinition>();
            AbilityLoadout loadout = ScriptableObject.CreateInstance<AbilityLoadout>();

            try
            {
                SetField(ability, "abilityId", "test.sequence.step");
                SetField(ability, "requiresTarget", false);
                SetField(sequence, "steps", new[] { ability });
                SetField(loadout, "sequences", new[] { sequence });

                AbilitySystem system = owner.AddComponent<AbilitySystem>();
                SetField(system, "loadout", loadout);

                bool canActivate = system.CanActivateSequence(
                    sequence,
                    AbilityContext.FromTarget(owner, null),
                    out string rejectionReason);

                Assert.IsTrue(canActivate, rejectionReason);
                Assert.IsFalse(system.IsActive);
                Assert.IsNull(system.ActiveSequence);
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(ability);
                Object.DestroyImmediate(sequence);
                Object.DestroyImmediate(loadout);
            }
        }

        [Test]
        public void CanActivateSequence_ExplainsMissingLoadoutGrant()
        {
            GameObject owner = new("AbilitySystemOwner");
            AbilitySequenceDefinition sequence =
                ScriptableObject.CreateInstance<AbilitySequenceDefinition>();

            try
            {
                AbilitySystem system = owner.AddComponent<AbilitySystem>();

                bool canActivate = system.CanActivateSequence(
                    sequence,
                    AbilityContext.FromTarget(owner, null),
                    out string rejectionReason);

                Assert.IsFalse(canActivate);
                Assert.AreEqual(
                    "Sequence is not granted by the current loadout.",
                    rejectionReason);
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(sequence);
            }
        }

        [Test]
        public void QueuedInputBeforeContinuationFrameStartsNextStepWhenFrameArrives()
        {
            GameObject owner = new("AbilitySystemOwner");
            AbilityDefinition first = ScriptableObject.CreateInstance<AbilityDefinition>();
            AbilityDefinition second = ScriptableObject.CreateInstance<AbilityDefinition>();
            AbilitySequenceDefinition sequence =
                ScriptableObject.CreateInstance<AbilitySequenceDefinition>();
            AbilityLoadout loadout = ScriptableObject.CreateInstance<AbilityLoadout>();

            try
            {
                ConfigureStep(first, "test.sequence.first");
                ConfigureStep(second, "test.sequence.second");
                first.ConfigureActionWindowsForTests(0, 2, 10);
                SetField(sequence, "steps", new[] { first, second });
                SetField(sequence, "advancement", SequenceAdvancement.Manual);
                SetField(loadout, "sequences", new[] { sequence });

                AbilitySystem system = owner.AddComponent<AbilitySystem>();
                SetField(system, "loadout", loadout);

                Assert.IsTrue(system.TryActivateSequence(
                    sequence,
                    AbilityContext.FromTarget(owner, null)));
                Assert.IsTrue(system.TryQueueSequenceAdvance());
                Assert.AreEqual(first, system.ActiveAbility);

                system.Tick(1f / 60f);

                Assert.AreEqual(second, system.ActiveAbility);
                Assert.AreEqual(sequence, system.ActiveSequence);
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
                Object.DestroyImmediate(sequence);
                Object.DestroyImmediate(loadout);
            }
        }

        [Test]
        public void InputWindowExpiresWithoutCancellingTheCurrentAttack()
        {
            GameObject owner = new("AbilitySystemOwner");
            AbilityDefinition first = ScriptableObject.CreateInstance<AbilityDefinition>();
            AbilityDefinition second = ScriptableObject.CreateInstance<AbilityDefinition>();
            AbilitySequenceDefinition sequence =
                ScriptableObject.CreateInstance<AbilitySequenceDefinition>();
            AbilityLoadout loadout = ScriptableObject.CreateInstance<AbilityLoadout>();

            try
            {
                ConfigureStep(first, "test.sequence.first");
                ConfigureStep(second, "test.sequence.second");
                first.ConfigureActionWindowsForTests(0, 5, 6);
                SetField(sequence, "steps", new[] { first, second });
                SetField(sequence, "advancement", SequenceAdvancement.Manual);
                SetField(loadout, "sequences", new[] { sequence });

                AbilitySystem system = owner.AddComponent<AbilitySystem>();
                SetField(system, "loadout", loadout);

                Assert.IsTrue(system.TryActivateSequence(
                    sequence,
                    AbilityContext.FromTarget(owner, null)));

                system.Tick(6f / 60f);

                Assert.IsNull(system.ActiveSequence);
                Assert.AreEqual(first, system.ActiveAbility);
                Assert.IsFalse(system.TryQueueSequenceAdvance());
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
                Object.DestroyImmediate(sequence);
                Object.DestroyImmediate(loadout);
            }
        }

        [Test]
        public void MovementUnlockFrameReleasesMovementBeforeAbilityCompletion()
        {
            GameObject owner = new("AbilitySystemOwner");
            AbilityDefinition ability = ScriptableObject.CreateInstance<AbilityDefinition>();
            AbilityLoadout loadout = ScriptableObject.CreateInstance<AbilityLoadout>();

            try
            {
                ConfigureStep(ability, "test.movement.unlock");
                ability.ConfigureActionWindowsForTests(2, 0, 0);
                SetField(loadout, "abilities", new[] { ability });

                AbilitySystem system = owner.AddComponent<AbilitySystem>();
                SetField(system, "loadout", loadout);

                Assert.IsTrue(system.TryActivate(
                    ability,
                    AbilityContext.FromTarget(owner, null)));
                Assert.IsTrue(system.IsMovementLocked);

                system.Tick(1f / 60f);

                Assert.IsFalse(system.IsMovementLocked);
                Assert.AreEqual(ability, system.ActiveAbility);
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(ability);
                Object.DestroyImmediate(loadout);
            }
        }

        [Test]
        public void ConfiguredInputEndDoesNotOpenPostCompletionWindowWithoutInput()
        {
            GameObject owner = new("AbilitySystemOwner");
            AbilityDefinition first = ScriptableObject.CreateInstance<AbilityDefinition>();
            AbilityDefinition second = ScriptableObject.CreateInstance<AbilityDefinition>();
            AbilitySequenceDefinition sequence =
                ScriptableObject.CreateInstance<AbilitySequenceDefinition>();
            AbilityLoadout loadout = ScriptableObject.CreateInstance<AbilityLoadout>();

            try
            {
                ConfigureStep(first, "test.sequence.first");
                ConfigureStep(second, "test.sequence.second");
                SetField(first, "recoveryEndFrame", 2);
                first.ConfigureActionWindowsForTests(0, 2, 2);
                SetField(sequence, "steps", new[] { first, second });
                SetField(sequence, "advancement", SequenceAdvancement.Manual);
                SetField(sequence, "manualAdvanceWindow", 1f);
                SetField(loadout, "sequences", new[] { sequence });

                AbilitySystem system = owner.AddComponent<AbilitySystem>();
                SetField(system, "loadout", loadout);

                Assert.IsTrue(system.TryActivateSequence(
                    sequence,
                    AbilityContext.FromTarget(owner, null)));

                system.Tick(2f / 60f);

                Assert.IsNull(system.ActiveSequence);
                Assert.IsFalse(system.IsAwaitingSequenceAdvance);
                Assert.IsFalse(system.TryQueueSequenceAdvance());
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
                Object.DestroyImmediate(sequence);
                Object.DestroyImmediate(loadout);
            }
        }

        private static void ConfigureStep(AbilityDefinition ability, string abilityId)
        {
            SetField(ability, "abilityId", abilityId);
            SetField(ability, "requiresTarget", false);
        }

        private static void SetField<TTarget, TValue>(
            TTarget target,
            string fieldName,
            TValue value)
        {
            FieldInfo field = typeof(TTarget).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, fieldName);
            field.SetValue(target, value);
        }
    }
}
