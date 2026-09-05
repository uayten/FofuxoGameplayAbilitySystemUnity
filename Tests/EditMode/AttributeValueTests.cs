using NUnit.Framework;
using UnityEngine;
using Fofuxo.GameplayAbilitySystem;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    public sealed class AttributeValueTests
    {
        private static readonly GameplayAttribute Health = new("Combat.Health");

        [Test]
        public void AggregationOrder_IsBaseThenAdditiveThenMultiplicativeThenOverride()
        {
            var value = new AttributeValue(100f, 0f, 1000f);
            value.AddModifier(new AttributeModifier(Health, AttributeOperation.Add, 20f));
            value.AddModifier(new AttributeModifier(Health, AttributeOperation.Multiply, 0.5f));
            Assert.AreEqual(180f, value.CurrentValue, 0.0001f);

            value.AddModifier(new AttributeModifier(Health, AttributeOperation.Override, 42f));
            Assert.AreEqual(42f, value.CurrentValue, 0.0001f);
        }

        [Test]
        public void CurrentValue_ClampsToLimits()
        {
            var value = new AttributeValue(100f, 0f, 150f);
            value.AddModifier(new AttributeModifier(Health, AttributeOperation.Add, 100f));
            Assert.AreEqual(150f, value.CurrentValue, 0.0001f);
        }

        [Test]
        public void InstantDamage_AndHeal_FoldIntoBase_AndRaiseChanged()
        {
            GameObject owner = new("AttributeSetOwner");
            try
            {
                AttributeSet set = owner.AddComponent<AttributeSet>();
                set.SetInitialValues(new[]
                {
                    CreateInitial(Health, 100f, 0f, 100f),
                });

                AttributeValueChanged lastChange = default;
                int changeCount = 0;
                set.Changed += change =>
                {
                    changeCount++;
                    lastChange = change;
                };

                set.ApplyInstantModifier(new AttributeModifier(Health, AttributeOperation.Add, -30f));
                Assert.AreEqual(70f, set.GetCurrent(Health), 0.0001f);
                Assert.AreEqual(1, changeCount);
                Assert.AreEqual(100f, lastChange.OldValue, 0.0001f);
                Assert.AreEqual(70f, lastChange.NewValue, 0.0001f);

                set.ApplyInstantModifier(new AttributeModifier(Health, AttributeOperation.Add, 50f));
                Assert.AreEqual(100f, set.GetCurrent(Health), 0.0001f);
                Assert.AreEqual(2, changeCount);
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void ClampedNoOp_RaisesNoChangedEvent_AndEmptyAttribute_IsIgnored()
        {
            GameObject owner = new("AttributeSetOwner");
            try
            {
                AttributeSet set = owner.AddComponent<AttributeSet>();
                set.SetInitialValues(new[]
                {
                    CreateInitial(Health, 0f, 0f, 100f),
                });

                int changeCount = 0;
                set.Changed += _ => changeCount++;

                set.ApplyInstantModifier(new AttributeModifier(Health, AttributeOperation.Add, -10f));
                set.ApplyInstantModifier(new AttributeModifier(default, AttributeOperation.Add, 10f));
                Assert.AreEqual(0, changeCount);
                Assert.AreEqual(0f, set.GetCurrent(Health), 0.0001f);
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void MissingAttribute_AutoCreates_AtZero()
        {
            GameObject owner = new("AttributeSetOwner");
            try
            {
                AttributeSet set = owner.AddComponent<AttributeSet>();
                Assert.AreEqual(0f, set.GetCurrent(new GameplayAttribute("Combat.Stamina")), 0.0001f);
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        private static AttributeSet.InitialValue CreateInitial(
            GameplayAttribute attribute, float baseValue, float minValue, float maxValue)
        {
            // InitialValue is a struct: set every field on one boxed copy, then unbox.
            object box = new AttributeSet.InitialValue();
            SetField(box, "attribute", attribute);
            SetField(box, "baseValue", baseValue);
            SetField(box, "minValue", minValue);
            SetField(box, "maxValue", maxValue);
            return (AttributeSet.InitialValue)box;
        }

        private static void SetField<TValue>(object box, string fieldName, TValue value)
        {
            var field = box.GetType().GetField(
                fieldName,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(field, fieldName);
            field.SetValue(box, value);
        }
    }
}
