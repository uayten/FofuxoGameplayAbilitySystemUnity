using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Fofuxo.GameplayAbilitySystem;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    public sealed class PeriodicAttributeEffectTests
    {
        private static readonly GameplayAttribute Health = new("Test.Health");

        private readonly List<Object> owned = new();

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
        }

        [Test]
        public void TicksApplyInstantDamage_PerPeriod_UntilExpiry()
        {
            AttributeSet set = NewSet(100f);
            Assert.IsTrue(set.ApplyPeriodicModifier(
                new AttributeModifier(Health, AttributeOperation.Add, -10f),
                2.5f,
                1f,
                EffectStacking.Stack));

            set.Tick(1f);
            Assert.AreEqual(90f, set.GetCurrent(Health));
            set.Tick(1f);
            Assert.AreEqual(80f, set.GetCurrent(Health));
            set.Tick(0.5f);
            Assert.AreEqual(80f, set.GetCurrent(Health));
        }

        [Test]
        public void EachTick_FiresChangeEvent()
        {
            AttributeSet set = NewSet(100f);
            set.ApplyPeriodicModifier(
                new AttributeModifier(Health, AttributeOperation.Add, -10f),
                3f,
                1f,
                EffectStacking.Stack);

            int changes = 0;
            set.Changed += _ => changes++;
            set.Tick(1f);
            set.Tick(1f);
            set.Tick(1f);

            Assert.AreEqual(80f, set.GetCurrent(Health));
            Assert.AreEqual(2, changes);
        }

        [Test]
        public void RefreshStacking_RestartsDuration()
        {
            AttributeSet set = NewSet(100f);
            var modifier = new AttributeModifier(Health, AttributeOperation.Add, -10f);
            set.ApplyPeriodicModifier(modifier, 1.5f, 1f, EffectStacking.Refresh);
            set.Tick(1f);
            Assert.AreEqual(90f, set.GetCurrent(Health));

            set.ApplyPeriodicModifier(modifier, 1.5f, 1f, EffectStacking.Refresh);
            set.Tick(1f);
            Assert.AreEqual(80f, set.GetCurrent(Health));
            set.Tick(0.5f);
            Assert.AreEqual(80f, set.GetCurrent(Health));
        }

        [Test]
        public void IgnoreStacking_KeepsFirstApplication()
        {
            AttributeSet set = NewSet(100f);
            var modifier = new AttributeModifier(Health, AttributeOperation.Add, -10f);
            set.ApplyPeriodicModifier(modifier, 2f, 1f, EffectStacking.Ignore);
            set.Tick(1f);
            set.ApplyPeriodicModifier(modifier, 5f, 1f, EffectStacking.Ignore);
            set.Tick(1f);

            Assert.AreEqual(90f, set.GetCurrent(Health));
        }

        [Test]
        public void ZeroDuration_CollapsesToInstant()
        {
            AttributeSet set = NewSet(100f);
            set.ApplyPeriodicModifier(
                new AttributeModifier(Health, AttributeOperation.Add, -10f),
                0f,
                1f,
                EffectStacking.Stack);

            Assert.AreEqual(90f, set.GetCurrent(Health));
            set.Tick(10f);
            Assert.AreEqual(90f, set.GetCurrent(Health));
        }

        [Test]
        public void EffectApply_AttachesPeriodic_ToTargetSet()
        {
            GameObject owner = NewOwner();
            AttributeSet set = NewSet(100f);
            PeriodicAttributeEffectDefinition effect =
                ScriptableObject.CreateInstance<PeriodicAttributeEffectDefinition>();
            owned.Add(effect);
            SetEffect(effect, "applyToTarget", true);
            SetEffect(effect, "attribute", Health);
            SetEffect(effect, "operation", AttributeOperation.Add);
            SetEffect(effect, "magnitudePerTick", -5f);
            SetEffect(effect, "durationSeconds", 2f);
            SetEffect(effect, "periodSeconds", 1f);
            SetEffect(effect, "stacking", EffectStacking.Refresh);

            Apply(owner, set.gameObject, effect);
            set.Tick(1f);
            set.Tick(1f);

            Assert.AreEqual(95f, set.GetCurrent(Health));
        }

        private GameObject NewOwner()
        {
            GameObject owner = new("EffectOwner");
            owned.Add(owner);
            owner.AddComponent<AbilitySystem>();
            return owner;
        }

        private AttributeSet NewSet(float health)
        {
            GameObject target = new("EffectTarget");
            owned.Add(target);
            AttributeSet set = target.AddComponent<AttributeSet>();
            set.SetInitialValues(new[]
            {
                new AttributeSet.InitialValue(Health, health, 0f, 100f),
            });
            return set;
        }

        private void Apply(GameObject owner, GameObject target, PeriodicAttributeEffectDefinition effect)
        {
            AbilitySystem system = owner.GetComponent<AbilitySystem>();
            AbilityDefinition definition = ScriptableObject.CreateInstance<AbilityDefinition>();
            owned.Add(definition);
            AbilityContext context = AbilityContext.FromTarget(owner, target);
            AbilityInstance instance = new(definition, context);
            effect.Apply(new AbilityEffectContext(system, instance, 0));
        }

        private static void SetEffect<TValue>(
            PeriodicAttributeEffectDefinition effect, string fieldName, TValue value)
        {
            var field = typeof(PeriodicAttributeEffectDefinition).GetField(
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
