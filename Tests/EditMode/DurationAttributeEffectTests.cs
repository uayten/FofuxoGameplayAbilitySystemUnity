using NUnit.Framework;
using UnityEngine;
using Fofuxo.GameplayAbilitySystem;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    public sealed class DurationAttributeEffectTests
    {
        private static readonly GameplayAttribute Health = new("Test.Health");

        [Test]
        public void DurationModifier_ExpiresAndDetaches()
        {
            AttributeSet set = NewSet(100f, 200f);
            try
            {
                Assert.IsTrue(set.ApplyDurationModifier(
                    new AttributeModifier(Health, AttributeOperation.Add, 30f),
                    2f,
                    EffectStacking.Stack));
                Assert.AreEqual(130f, set.GetCurrent(Health));
                set.Tick(1f);
                Assert.AreEqual(130f, set.GetCurrent(Health));
                set.Tick(1.5f);
                Assert.AreEqual(100f, set.GetCurrent(Health));
            }
            finally
            {
                Object.DestroyImmediate(set.gameObject);
            }
        }

        [Test]
        public void EffectApply_AttachesDuration_ToTargetSet()
        {
            GameObject owner = new("EffectOwner");
            AttributeSet set = NewSet(100f, 200f);
            DurationAttributeEffectDefinition effect =
                ScriptableObject.CreateInstance<DurationAttributeEffectDefinition>();
            try
            {
                SetEffect(effect, "applyToTarget", true);
                SetEffect(effect, "attribute", Health);
                SetEffect(effect, "operation", AttributeOperation.Add);
                SetEffect(effect, "magnitude", 25f);
                SetEffect(effect, "durationSeconds", 2f);
                SetEffect(effect, "stacking", EffectStacking.Refresh);

                Apply(owner, set.gameObject, effect);
                Assert.AreEqual(125f, set.GetCurrent(Health));
                set.Tick(2.5f);
                Assert.AreEqual(100f, set.GetCurrent(Health));
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(set.gameObject);
                Object.DestroyImmediate(effect);
            }
        }

        private AttributeSet NewSet(float health, float maxHealth)
        {
            GameObject target = new("EffectTarget");
            AttributeSet set = target.AddComponent<AttributeSet>();
            set.SetInitialValues(new[]
            {
                new AttributeSet.InitialValue(Health, health, 0f, maxHealth),
            });
            return set;
        }

        private void Apply(GameObject owner, GameObject target, DurationAttributeEffectDefinition effect)
        {
            AbilitySystem system = owner.GetComponent<AbilitySystem>();
            if (system == null)
            {
                system = owner.AddComponent<AbilitySystem>();
            }

            AbilityDefinition definition = ScriptableObject.CreateInstance<AbilityDefinition>();
            AbilityContext context = AbilityContext.FromTarget(owner, target);
            AbilityInstance instance = new(definition, context);
            effect.Apply(new AbilityEffectContext(system, instance, 0));
            Object.DestroyImmediate(definition);
        }

        private static void SetEffect<TValue>(
            DurationAttributeEffectDefinition effect, string fieldName, TValue value)
        {
            var field = typeof(DurationAttributeEffectDefinition).GetField(
                fieldName,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(field, fieldName);
            object boxedValue = value;
            if (field.FieldType.IsEnum)
            {
                boxedValue = System.Enum.ToObject(field.FieldType, value);
            }

            field.SetValue(effect, boxedValue);
        }
    }
}
