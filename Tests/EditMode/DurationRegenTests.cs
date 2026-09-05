using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Fofuxo.GameplayAbilitySystem;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    public sealed class DurationRegenTests
    {
        private static readonly GameplayAttribute Health = new("Combat.Health");

        private GameObject owner;
        private AttributeSet set;
        private readonly List<Object> owned = new();

        [SetUp]
        public void SetUp()
        {
            owner = new GameObject("DurationOwner");
            set = owner.AddComponent<AttributeSet>();
            set.SetInitialValues(new[]
            {
                new AttributeSet.InitialValue(Health, 100f, 0f, 100f),
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
        public void DurationModifier_ExpiresAfterDuration()
        {
            int changeCount = 0;
            set.Changed += _ => changeCount++;

            set.ApplyInstantModifier(new AttributeModifier(Health, AttributeOperation.Add, -40f));
            Assert.AreEqual(60f, set.GetCurrent(Health), 0.0001f);

            Assert.IsTrue(set.ApplyDurationModifier(
                new AttributeModifier(Health, AttributeOperation.Add, 20f),
                2f,
                EffectStacking.Stack));
            Assert.AreEqual(80f, set.GetCurrent(Health), 0.0001f);

            set.Tick(1f);
            Assert.AreEqual(80f, set.GetCurrent(Health), 0.0001f);

            set.Tick(1.5f);
            Assert.AreEqual(60f, set.GetCurrent(Health), 0.0001f);
            Assert.AreEqual(3, changeCount);
        }

        [Test]
        public void Refresh_RenewsRemaining_WithoutStacking()
        {
            Assert.IsTrue(set.ApplyDurationModifier(
                new AttributeModifier(Health, AttributeOperation.Add, -30f),
                2f,
                EffectStacking.Stack));
            Assert.AreEqual(70f, set.GetCurrent(Health), 0.0001f);

            set.Tick(1.5f);
            Assert.IsTrue(set.ApplyDurationModifier(
                new AttributeModifier(Health, AttributeOperation.Add, -30f),
                2f,
                EffectStacking.Refresh));
            Assert.AreEqual(70f, set.GetCurrent(Health), 0.0001f);

            set.Tick(1.5f);
            Assert.AreEqual(70f, set.GetCurrent(Health), 0.0001f);

            set.Tick(1f);
            Assert.AreEqual(100f, set.GetCurrent(Health), 0.0001f);
        }

        [Test]
        public void Ignore_KeepsFirstApplication()
        {
            Assert.IsTrue(set.ApplyDurationModifier(
                new AttributeModifier(Health, AttributeOperation.Add, -30f),
                2f,
                EffectStacking.Stack));
            Assert.IsTrue(set.ApplyDurationModifier(
                new AttributeModifier(Health, AttributeOperation.Add, -30f),
                2f,
                EffectStacking.Ignore));

            set.Tick(2.5f);
            Assert.AreEqual(100f, set.GetCurrent(Health), 0.0001f);
        }

        [Test]
        public void Regeneration_AccumulatesOverTicks_AndClamps()
        {
            SetField(set, "regeneration", new[]
            {
                new AttributeSet.Regeneration(Health, 5f),
            });
            set.ApplyInstantModifier(new AttributeModifier(Health, AttributeOperation.Add, -30f));
            Assert.AreEqual(70f, set.GetCurrent(Health), 0.0001f);

            set.Tick(2f);
            Assert.AreEqual(80f, set.GetCurrent(Health), 0.0001f);

            set.Tick(10f);
            Assert.AreEqual(100f, set.GetCurrent(Health), 0.0001f);
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
