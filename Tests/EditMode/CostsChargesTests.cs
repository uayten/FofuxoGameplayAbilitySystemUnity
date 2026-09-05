using NUnit.Framework;
using UnityEngine;
using Fofuxo.GameplayAbilitySystem;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    public sealed class CostsChargesTests
    {
        private static readonly GameplayAttribute Stamina = new("Combat.Stamina");

        private GameObject owner;
        private AbilitySystem system;
        private AttributeSet attributes;
        private readonly System.Collections.Generic.List<Object> owned = new();

        [SetUp]
        public void SetUp()
        {
            owner = new GameObject("CostsOwner");
            system = owner.AddComponent<AbilitySystem>();
            attributes = owner.AddComponent<AttributeSet>();
            attributes.SetInitialValues(new[]
            {
                Initial(Stamina, 100f, 0f, 100f),
            });
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object ownedObject in owned)
            {
                if (ownedObject != null)
                {
                    Object.DestroyImmediate(ownedObject);
                }
            }

            owned.Clear();
            Object.DestroyImmediate(owner);
        }

        [Test]
        public void Cost_IsCheckedBeforeActivation_AndDeductedOnSuccess()
        {
            AbilityDefinition ability = NewAbility("test.costly");
            SetField(ability, "costs", new[] { new AbilityCost(Stamina, 30f) });
            Grant(ability);

            AbilityContext context = AbilityContext.FromTarget(owner, null);
            Assert.IsTrue(system.TryActivate(ability, context));
            Assert.AreEqual(70f, attributes.GetCurrent(Stamina), 0.0001f);

            system.ForceCancelActiveAbility(AbilityCancelReason.Manual);
            Assert.IsTrue(system.TryActivate(ability, context));
            Assert.AreEqual(40f, attributes.GetCurrent(Stamina), 0.0001f);

            system.ForceCancelActiveAbility(AbilityCancelReason.Manual);
            Assert.IsTrue(system.TryActivate(ability, context));
            Assert.AreEqual(10f, attributes.GetCurrent(Stamina), 0.0001f);

            system.ForceCancelActiveAbility(AbilityCancelReason.Manual);
            Assert.IsFalse(
                system.CanActivate(ability, context, out string reason));
            Assert.IsTrue(reason.Contains("Insufficient"), reason);
        }

        [Test]
        public void Charges_AreConsumed_AndBlockWhenExhausted()
        {
            AbilityDefinition ability = NewAbility("test.charged");
            SetField(ability, "maxCharges", 1);
            SetField(ability, "chargeRestoreTime", 3600f);
            Grant(ability);

            AbilityContext context = AbilityContext.FromTarget(owner, null);
            Assert.IsTrue(system.TryActivate(ability, context));
            system.ForceCancelActiveAbility(AbilityCancelReason.Manual);
            Assert.IsFalse(
                system.CanActivate(ability, context, out string reason));
            Assert.IsTrue(reason.Contains("charges"), reason);
        }

        [Test]
        public void TryValidate_RejectsEmptyCost_AndChargelessRestore()
        {
            AbilityDefinition emptyCost = NewAbility("test.empty.cost");
            SetField(emptyCost, "costs", new[] { new AbilityCost(default, 10f) });
            Assert.IsFalse(emptyCost.TryValidate(out string costError));
            Assert.IsTrue(costError.Contains("Cost"), costError);
            Object.DestroyImmediate(emptyCost);

            AbilityDefinition stranded = NewAbility("test.stranded");
            SetField(stranded, "maxCharges", 2);
            Assert.IsFalse(stranded.TryValidate(out string chargeError));
            Assert.IsTrue(chargeError.Contains("charges"), chargeError);
            Object.DestroyImmediate(stranded);
        }

        private AbilityDefinition NewAbility(string id)
        {
            AbilityDefinition ability = ScriptableObject.CreateInstance<AbilityDefinition>();
            owned.Add(ability);
            SetField(ability, "abilityId", id);
            SetField(ability, "requiresTarget", false);
            return ability;
        }

        private void Grant(AbilityDefinition ability)
        {
            AbilityLoadout loadout = ScriptableObject.CreateInstance<AbilityLoadout>();
            owned.Add(loadout);
            SetField(loadout, "abilities", new[] { ability });
            SetField(system, "loadout", loadout);
        }

        private static AttributeSet.InitialValue Initial(
            GameplayAttribute attribute, float baseValue, float minValue, float maxValue)
        {
            return new AttributeSet.InitialValue(attribute, baseValue, minValue, maxValue);
        }

        private static void SetField<TValue>(object target, string fieldName, TValue value)
        {
            var field = target.GetType().GetField(
                fieldName,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(field, fieldName);
            field.SetValue(target, value);
        }
    }
}
