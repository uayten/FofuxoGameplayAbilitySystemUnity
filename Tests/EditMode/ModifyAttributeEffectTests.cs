using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Fofuxo.GameplayAbilitySystem;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    public sealed class ModifyAttributeEffectTests
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
        public void NegativeAdd_DamagesTargetSet_AndFiresChange()
        {
            GameObject owner = NewOwner();
            AttributeSet set = NewSet(100f);
            ModifyAttributeEffectDefinition effect = NewEffect();
            SetEffect(effect, "applyToTarget", true);
            SetEffect(effect, "attribute", Health);
            SetEffect(effect, "operation", AttributeOperation.Add);
            SetEffect(effect, "magnitude", -25f);

            AttributeValueChanged? observed = null;
            set.Changed += change => observed = change;
            Apply(owner, set.gameObject, effect);

            Assert.AreEqual(75f, set.GetCurrent(Health));
            Assert.IsTrue(observed.HasValue);
            Assert.AreEqual(100f, observed.Value.OldValue);
            Assert.AreEqual(75f, observed.Value.NewValue);
            Assert.AreEqual(owner, observed.Value.Source);
        }

        [Test]
        public void ApplyToOwner_HealsOwnerSet()
        {
            GameObject owner = NewOwner();
            AttributeSet set = owner.AddComponent<AttributeSet>();
            set.SetInitialValues(new[]
            {
                new AttributeSet.InitialValue(Health, 40f, 0f, 100f),
            });
            ModifyAttributeEffectDefinition effect = NewEffect();
            SetEffect(effect, "applyToTarget", false);
            SetEffect(effect, "attribute", Health);
            SetEffect(effect, "operation", AttributeOperation.Add);
            SetEffect(effect, "magnitude", 10f);

            Apply(owner, null, effect);

            Assert.AreEqual(50f, set.GetCurrent(Health));
        }

        [Test]
        public void MissingSet_IsIgnored()
        {
            GameObject owner = NewOwner();
            GameObject target = new("EffectTarget");
            owned.Add(target);
            ModifyAttributeEffectDefinition effect = NewEffect();
            SetEffect(effect, "applyToTarget", true);
            SetEffect(effect, "attribute", Health);
            SetEffect(effect, "operation", AttributeOperation.Add);
            SetEffect(effect, "magnitude", -25f);

            Assert.DoesNotThrow(() => Apply(owner, target, effect));
        }

        [Test]
        public void EmptyAttribute_IsIgnored()
        {
            GameObject owner = NewOwner();
            AttributeSet set = NewSet(100f);
            ModifyAttributeEffectDefinition effect = NewEffect();
            SetEffect(effect, "applyToTarget", true);
            SetEffect(effect, "attribute", new GameplayAttribute());
            SetEffect(effect, "operation", AttributeOperation.Add);
            SetEffect(effect, "magnitude", -25f);

            Apply(owner, set.gameObject, effect);

            Assert.AreEqual(100f, set.GetCurrent(Health));
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

        private ModifyAttributeEffectDefinition NewEffect()
        {
            ModifyAttributeEffectDefinition effect =
                ScriptableObject.CreateInstance<ModifyAttributeEffectDefinition>();
            owned.Add(effect);
            return effect;
        }

        private void Apply(GameObject owner, GameObject target, ModifyAttributeEffectDefinition effect)
        {
            AbilitySystem system = owner.GetComponent<AbilitySystem>();
            AbilityDefinition definition = ScriptableObject.CreateInstance<AbilityDefinition>();
            owned.Add(definition);
            AbilityContext context = AbilityContext.FromTarget(owner, target);
            AbilityInstance instance = new(definition, context);
            effect.Apply(new AbilityEffectContext(system, instance, 0));
        }

        private static void SetEffect<TValue>(
            ModifyAttributeEffectDefinition effect, string fieldName, TValue value)
        {
            var field = typeof(ModifyAttributeEffectDefinition).GetField(
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
