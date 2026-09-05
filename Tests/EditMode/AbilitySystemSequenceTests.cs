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
