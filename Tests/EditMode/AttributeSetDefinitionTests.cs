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
                new[] { new AttributeSet.InitialValue(Health, 40f, 0f, 100f) });
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
        public void SetDefinition_Rebuilds_AndDropsAttachedModifiers()
        {
            AttributeSetDefinition definition = NewDefinition(
                new[] { new AttributeSet.InitialValue(Health, 100f, 0f, 200f) });
            GameObject owner = new("DefOwner");
            try
            {
                AttributeSet set = owner.AddComponent<AttributeSet>();
                set.SetInitialValues(new[]
                {
                    new AttributeSet.InitialValue(Health, 100f, 0f, 200f),
                });
                int slot = set.AddModifier(
                    new AttributeModifier(Health, AttributeOperation.Add, 50f));
                Assert.AreEqual(150f, set.GetCurrent(Health));

                set.SetDefinition(definition);
                Assert.AreEqual(100f, set.GetCurrent(Health));
                Assert.IsFalse(set.RemoveModifier(slot), "The rebuild dropped the slot.");
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
            AttributeSet.InitialValue[] initials)
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
            }

            return definition;
        }
    }
}
