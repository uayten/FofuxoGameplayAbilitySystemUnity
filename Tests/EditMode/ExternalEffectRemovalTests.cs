using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Fofuxo.GameplayAbilitySystem;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    public sealed class ExternalEffectRemovalTests
    {
        private static readonly GameplayAttribute Health = new("Test.Health");
        private static readonly GameplayAttribute Armor = new("Test.Armor");

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
        public void DurationEntry_DetachesValue_AndFiresChanged()
        {
            AttributeSet set = NewSet(100f, 200f);
            Assert.IsTrue(set.ApplyDurationModifier(
                new AttributeModifier(Health, AttributeOperation.Add, 20f),
                5f,
                EffectStacking.Stack));
            Assert.AreEqual(120f, set.GetCurrent(Health));

            int changes = 0;
            set.Changed += _ => changes++;

            Assert.AreEqual(1, set.RemoveModifiers(Health));
            Assert.AreEqual(100f, set.GetCurrent(Health));
            Assert.AreEqual(1, changes);

            set.Tick(10f);
            Assert.AreEqual(100f, set.GetCurrent(Health));
        }

        [Test]
        public void PeriodicEntry_StopsFutureTicks_WithoutRefundingAppliedTicks()
        {
            AttributeSet set = NewSet(100f, 100f);
            set.ApplyPeriodicModifier(
                new AttributeModifier(Health, AttributeOperation.Add, -10f),
                5f,
                1f,
                EffectStacking.Stack);
            set.Tick(1f);
            Assert.AreEqual(90f, set.GetCurrent(Health));

            int changes = 0;
            set.Changed += _ => changes++;

            Assert.AreEqual(1, set.RemoveModifiers(Health));
            Assert.AreEqual(0, changes);

            set.Tick(5f);
            Assert.AreEqual(90f, set.GetCurrent(Health));
        }

        [Test]
        public void SourceFilter_OnlyRemovesMatchingSource()
        {
            AttributeSet set = NewSet(100f, 100f);
            GameObject sourceA = NewSource("SourceA");
            GameObject sourceB = NewSource("SourceB");
            set.ApplyPeriodicModifier(
                new AttributeModifier(Health, AttributeOperation.Add, -10f, sourceA),
                5f,
                1f,
                EffectStacking.Stack);
            set.ApplyPeriodicModifier(
                new AttributeModifier(Health, AttributeOperation.Add, -10f, sourceB),
                5f,
                1f,
                EffectStacking.Stack);
            set.Tick(1f);
            Assert.AreEqual(80f, set.GetCurrent(Health));

            Assert.AreEqual(1, set.RemoveModifiers(Health, sourceA));

            set.Tick(1f);
            Assert.AreEqual(70f, set.GetCurrent(Health));
        }

        [Test]
        public void NoMatch_ReturnsZero_AndChangesNothing()
        {
            AttributeSet set = NewSet(100f, 100f);

            int changes = 0;
            set.Changed += _ => changes++;

            Assert.AreEqual(0, set.RemoveModifiers(Armor));
            Assert.AreEqual(0, set.RemoveModifiers(new GameplayAttribute()));
            Assert.AreEqual(100f, set.GetCurrent(Health));
            Assert.AreEqual(0, changes);
        }

        private GameObject NewSource(string name)
        {
            GameObject source = new(name);
            owned.Add(source);
            return source;
        }

        private AttributeSet NewSet(float health, float maxHealth)
        {
            GameObject target = new("EffectTarget");
            owned.Add(target);
            AttributeSet set = target.AddComponent<AttributeSet>();
            set.SetInitialValues(new[]
            {
                new AttributeSet.InitialValue(Health, health, 0f, maxHealth),
            });
            return set;
        }
    }
}
