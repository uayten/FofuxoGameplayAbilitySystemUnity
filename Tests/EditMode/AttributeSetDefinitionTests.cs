using NUnit.Framework;
using UnityEngine;
using Fofuxo.GameplayAbilitySystem;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    public sealed class AttributeSetDefinitionTests
    {
        private static readonly GameplayAttribute Health = new("Test.Health");

        [Test]
        public void Definition_ProvidesInitialValues()
        {
            AttributeSetDefinition definition = NewDefinition(
                new[] { new AttributeSet.InitialValue(Health, 40f, 0f, 100f) },
                new AttributeSet.Regeneration[] { });
            GameObject owner = new("DefOwner");
            try
            {
                AttributeSet set = owner.AddComponent<AttributeSet>();
                set.SetDefinition(definition);
                Assert.AreEqual(40f, set.GetCurrent(Health));
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(definition);
            }
        }

        [Test]
        public void SetDefinition_Rebuilds_AndDropsActiveDurationEntries()
        {
            AttributeSetDefinition definition = NewDefinition(
                new[] { new AttributeSet.InitialValue(Health, 100f, 0f, 200f) },
                new AttributeSet.Regeneration[] { });
            GameObject owner = new("DefOwner");
            try
            {
                AttributeSet set = owner.AddComponent<AttributeSet>();
                set.SetInitialValues(new[]
                {
                    new AttributeSet.InitialValue(Health, 100f, 0f, 200f),
                });
                Assert.IsTrue(set.ApplyDurationModifier(
                    new AttributeModifier(Health, AttributeOperation.Add, 50f),
                    10f,
                    EffectStacking.Stack));
                Assert.AreEqual(150f, set.GetCurrent(Health));

                set.SetDefinition(definition);
                Assert.AreEqual(100f, set.GetCurrent(Health));
                set.Tick(20f);
                Assert.AreEqual(100f, set.GetCurrent(Health));
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(definition);
            }
        }

        [Test]
        public void NullDefinition_FallsBackToLocalInitials()
        {
            GameObject owner = new("DefOwner");
            try
            {
                AttributeSet set = owner.AddComponent<AttributeSet>();
                set.SetInitialValues(new[]
                {
                    new AttributeSet.InitialValue(Health, 70f, 0f, 100f),
                });
                set.SetDefinition(null);
                Assert.AreEqual(70f, set.GetCurrent(Health));
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        private static AttributeSetDefinition NewDefinition(
            AttributeSet.InitialValue[] initials,
            AttributeSet.Regeneration[] regen)
        {
            AttributeSetDefinition definition =
                ScriptableObject.CreateInstance<AttributeSetDefinition>();
            var fields = typeof(AttributeSetDefinition).GetFields(
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            foreach (var field in fields)
            {
                if (field.Name == "initialValues")
                {
                    field.SetValue(definition, initials);
                }
                else if (field.Name == "regeneration")
                {
                    field.SetValue(definition, regen);
                }
            }

            return definition;
        }
    }
}
