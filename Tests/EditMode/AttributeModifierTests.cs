using NUnit.Framework;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    /// <summary>
    /// What the attribute set owns on its own: limits, a ceiling read from
    /// another attribute, and modifier slots with no lifetime of their own.
    /// Regeneration is a periodic effect now. Durations, periods and stacking moved to
    /// <see cref="GameplayEffectContainer"/> and are covered by the gameplay
    /// effect fixtures.
    /// </summary>
    public sealed class AttributeModifierTests
    {
        private static readonly GameplayAttribute Health = new("Combat.Health");

        private GameObject owner;
        private AttributeSet set;

        [SetUp]
        public void SetUp()
        {
            owner = new GameObject("AttributeOwner");
            set = owner.AddComponent<AttributeSet>();
            set.SetInitialValues(new[]
            {
                new AttributeSet.InitialValue(Health, 100f, 0f, 100f),
            });
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(owner);
        }

        [Test]
        public void AddModifier_ContributesUntilItsSlotIsRemoved()
        {
            set.ApplyInstantModifier(new AttributeModifier(Health, AttributeOperation.Add, -40f));
            Assert.AreEqual(60f, set.GetCurrent(Health), 0.0001f);

            int slot = set.AddModifier(new AttributeModifier(Health, AttributeOperation.Add, 20f));
            Assert.AreNotEqual(0, slot);
            Assert.AreEqual(80f, set.GetCurrent(Health), 0.0001f);
            Assert.AreEqual(60f, set.GetBase(Health), 0.0001f, "A modifier never touches the base.");

            Assert.IsTrue(set.RemoveModifier(slot));
            Assert.AreEqual(60f, set.GetCurrent(Health), 0.0001f);
            Assert.IsFalse(set.RemoveModifier(slot), "A slot is removed once.");
        }

        [Test]
        public void TwoIdenticalModifiers_AreTwoSlots()
        {
            set.ApplyInstantModifier(new AttributeModifier(Health, AttributeOperation.Add, -60f));
            AttributeModifier modifier = new(Health, AttributeOperation.Add, 20f);

            int first = set.AddModifier(modifier);
            int second = set.AddModifier(modifier);
            Assert.AreNotEqual(first, second);
            Assert.AreEqual(80f, set.GetCurrent(Health), 0.0001f);

            set.RemoveModifier(first);

            Assert.AreEqual(60f, set.GetCurrent(Health), 0.0001f,
                "Removing one identical modifier leaves the other attached.");
        }

        [Test]
        public void UpdateModifier_MovesTheContributionInPlace()
        {
            set.ApplyInstantModifier(new AttributeModifier(Health, AttributeOperation.Add, -50f));
            int slot = set.AddModifier(new AttributeModifier(Health, AttributeOperation.Add, 10f));
            Assert.AreEqual(60f, set.GetCurrent(Health), 0.0001f);

            Assert.IsTrue(set.UpdateModifier(
                slot, new AttributeModifier(Health, AttributeOperation.Add, 30f)));

            Assert.AreEqual(80f, set.GetCurrent(Health), 0.0001f);
        }

        [Test]
        public void ModifierChanges_FireTheChangeEvent()
        {
            int changeCount = 0;
            set.Changed += _ => changeCount++;

            set.ApplyInstantModifier(new AttributeModifier(Health, AttributeOperation.Add, -40f));
            int slot = set.AddModifier(new AttributeModifier(Health, AttributeOperation.Add, 20f));
            set.RemoveModifier(slot);

            Assert.AreEqual(3, changeCount);
        }

        [Test]
        public void MaxAttribute_CapsTheCurrentValue_AndAddStopsAtIt()
        {
            GameplayAttribute stamina = new("Combat.Stamina");
            GameplayAttribute maxStamina = new("Combat.MaxStamina");
            set.SetInitialValues(new[]
            {
                new AttributeSet.InitialValue(stamina, 100f, 0f, 0f, maxStamina),
                new AttributeSet.InitialValue(maxStamina, 100f, 0f, 1000f),
            });

            set.ApplyInstantModifier(new AttributeModifier(stamina, AttributeOperation.Add, 50f));
            Assert.AreEqual(100f, set.GetCurrent(stamina), 0.0001f, "a refill stops at the ceiling");
            Assert.AreEqual(100f, set.GetBase(stamina), 0.0001f);

            int raise = set.AddModifier(new AttributeModifier(maxStamina, AttributeOperation.Add, 40f));
            set.ApplyInstantModifier(new AttributeModifier(stamina, AttributeOperation.Add, 40f));
            Assert.AreEqual(140f, set.GetCurrent(stamina), 0.0001f, "a raised ceiling lets it fill further");

            set.RemoveModifier(raise);
            Assert.AreEqual(100f, set.GetCurrent(stamina), 0.0001f, "a lowered ceiling caps the read");
            Assert.AreEqual(140f, set.GetBase(stamina), 0.0001f, "and leaves the base for when it comes back");
        }

        [Test]
        public void MaxAttribute_Override_IsWrittenAsGiven_SoARestoreKeepsItsValue()
        {
            GameplayAttribute stamina = new("Combat.Stamina");
            GameplayAttribute maxStamina = new("Combat.MaxStamina");
            set.SetInitialValues(new[]
            {
                new AttributeSet.InitialValue(stamina, 100f, 0f, 0f, maxStamina),
                new AttributeSet.InitialValue(maxStamina, 100f, 0f, 1000f),
            });

            set.ApplyInstantModifier(new AttributeModifier(stamina, AttributeOperation.Override, 140f));
            Assert.AreEqual(140f, set.GetBase(stamina), 0.0001f);
            Assert.AreEqual(100f, set.GetCurrent(stamina), 0.0001f);

            set.AddModifier(new AttributeModifier(maxStamina, AttributeOperation.Add, 40f));
            Assert.AreEqual(140f, set.GetCurrent(stamina), 0.0001f,
                "the ceiling came back after the base, the way a save restores them");
        }

        [Test]
        public void MaxAttribute_MovingTheCeiling_RaisesAChangeForWhatItCaps()
        {
            GameplayAttribute stamina = new("Combat.Stamina");
            GameplayAttribute maxStamina = new("Combat.MaxStamina");
            set.SetInitialValues(new[]
            {
                new AttributeSet.InitialValue(stamina, 100f, 0f, 0f, maxStamina),
                new AttributeSet.InitialValue(maxStamina, 100f, 0f, 1000f),
            });

            float reported = -1f;
            set.Changed += change =>
            {
                if (change.Attribute == stamina)
                {
                    reported = change.NewValue;
                }
            };

            set.ApplyInstantModifier(new AttributeModifier(maxStamina, AttributeOperation.Add, -40f));
            Assert.AreEqual(60f, reported, 0.0001f);
        }

        private static void SetField<TValue>(object target, string fieldName, TValue value)
        {
            // Walks up the hierarchy: a private field of a base class is
            // invisible to a single GetField call, and an ability's own fields
            // sit one level above the timeline type most fixtures use.
            for (System.Type type = target.GetType(); type != null; type = type.BaseType)
            {
                System.Reflection.FieldInfo field = type.GetField(
                    fieldName,
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    field.SetValue(target, value);
                    return;
                }
            }

            Assert.Fail($"No field named {fieldName}.");
        }
    }
}
