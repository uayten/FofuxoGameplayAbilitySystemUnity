using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    public sealed class AbilityLoadoutValidationTests
    {
        private readonly List<Object> owned = new();
        private readonly List<AbilityLoadoutValidationIssue> issues = new();

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
            issues.Clear();
        }

        [Test]
        public void EmptyLoadout_IsValid()
        {
            AbilityLoadout loadout = NewLoadout();

            Assert.IsTrue(loadout.TryValidate(out string error), error);
            Assert.IsTrue(loadout.TryValidate(issues));
            Assert.IsEmpty(issues);
        }

        [Test]
        public void PopulatedLoadout_WithDistinctIds_IsValid()
        {
            AbilityLoadout loadout = NewLoadout(
                NewAbility("player.attack"),
                NewAbility("player.roll"));

            Assert.IsTrue(loadout.TryValidate(out string error), error);
            Assert.IsTrue(loadout.TryValidate(issues));
            Assert.IsEmpty(issues);
        }

        [Test]
        public void NullSlot_IsReported_WithItsSlotNumber()
        {
            AbilityLoadout loadout = NewLoadout(NewAbility("player.attack"), null);

            Assert.IsFalse(loadout.TryValidate(issues));
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(AbilityLoadoutIssue.EmptySlot, issues[0].Issue);
            Assert.AreEqual(1, issues[0].SlotIndex);
            Assert.IsNull(issues[0].Ability);
            Assert.IsTrue(issues[0].Message.Contains("Slot 2"), issues[0].Message);
        }

        [Test]
        public void EmptyAbilityId_IsReported_AndDoesNotAlsoReportTheDefinition()
        {
            TimelineAbilityDefinition ability = NewAbility(string.Empty);
            ability.name = "GA_Player_Nameless";
            AbilityLoadout loadout = NewLoadout(ability);

            Assert.IsFalse(loadout.TryValidate(issues));
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(AbilityLoadoutIssue.EmptyAbilityId, issues[0].Issue);
            Assert.AreEqual(0, issues[0].SlotIndex);
            Assert.AreSame(ability, issues[0].Ability);
            Assert.IsTrue(issues[0].Message.Contains("GA_Player_Nameless"), issues[0].Message);
        }

        [Test]
        public void WhitespaceAbilityId_CountsAsEmpty()
        {
            AbilityLoadout loadout = NewLoadout(NewAbility("   "));

            Assert.IsFalse(loadout.TryValidate(issues));
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(AbilityLoadoutIssue.EmptyAbilityId, issues[0].Issue);
        }

        [Test]
        public void DuplicateAbilityId_IsReported_OnTheSecondSlot()
        {
            TimelineAbilityDefinition first = NewAbility("player.attack");
            first.name = "GA_Player_Slash";
            TimelineAbilityDefinition second = NewAbility("player.attack");
            second.name = "GA_Player_Thrust";
            AbilityLoadout loadout = NewLoadout(first, second);

            Assert.IsFalse(loadout.TryValidate(issues));
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(AbilityLoadoutIssue.DuplicateAbilityId, issues[0].Issue);
            Assert.AreEqual(1, issues[0].SlotIndex);
            Assert.AreSame(second, issues[0].Ability);
            Assert.IsTrue(issues[0].Message.Contains("player.attack"), issues[0].Message);
            Assert.IsTrue(issues[0].Message.Contains("GA_Player_Slash"), issues[0].Message);

            // The rule exists because the second asset is unreachable by ID.
            Assert.AreSame(first, loadout.FindAbility("player.attack"));
        }

        [Test]
        public void IdsDifferingOnlyInCase_AreNotDuplicates()
        {
            // FindAbility compares Ordinal, so these really are two lookups.
            AbilityLoadout loadout = NewLoadout(
                NewAbility("player.attack"),
                NewAbility("Player.Attack"));

            Assert.IsTrue(loadout.TryValidate(issues));
            Assert.IsEmpty(issues);
        }

        [Test]
        public void SameAssetListedTwice_IsReportedAsADuplicateId()
        {
            TimelineAbilityDefinition ability = NewAbility("player.attack");
            AbilityLoadout loadout = NewLoadout(ability, ability);

            Assert.IsFalse(loadout.TryValidate(issues));
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(AbilityLoadoutIssue.DuplicateAbilityId, issues[0].Issue);
        }

        [Test]
        public void InvalidAbility_IsReported_WithTheDefinitionError()
        {
            TimelineAbilityDefinition ability = NewAbility("player.charged");
            SetField(ability, "maxCharges", 2);

            AbilityLoadout loadout = NewLoadout(ability);

            Assert.IsFalse(loadout.TryValidate(issues));
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(AbilityLoadoutIssue.InvalidAbility, issues[0].Issue);
            Assert.IsTrue(issues[0].Message.Contains("charges"), issues[0].Message);
        }

        [Test]
        public void FullReport_ListsEveryIssue_WhileTryValidateStopsAtTheFirst()
        {
            AbilityLoadout loadout = NewLoadout(
                null,
                NewAbility(string.Empty),
                NewAbility("player.attack"),
                NewAbility("player.attack"));

            Assert.IsFalse(loadout.TryValidate(out string error));
            Assert.IsTrue(error.Contains("Slot 1"), error);

            Assert.IsFalse(loadout.TryValidate(issues));
            Assert.AreEqual(3, issues.Count);
            Assert.AreEqual(AbilityLoadoutIssue.EmptySlot, issues[0].Issue);
            Assert.AreEqual(AbilityLoadoutIssue.EmptyAbilityId, issues[1].Issue);
            Assert.AreEqual(AbilityLoadoutIssue.DuplicateAbilityId, issues[2].Issue);
        }

        [Test]
        public void FullReport_ClearsTheListItProvides()
        {
            issues.Add(new AbilityLoadoutValidationIssue(
                AbilityLoadoutIssue.EmptySlot, 99, null, "stale"));

            AbilityLoadout loadout = NewLoadout(NewAbility("player.attack"));

            Assert.IsTrue(loadout.TryValidate(issues));
            Assert.IsEmpty(issues);
        }

        private AbilityLoadout NewLoadout(params AbilityDefinition[] abilities)
        {
            AbilityLoadout loadout = ScriptableObject.CreateInstance<AbilityLoadout>();
            owned.Add(loadout);
            SetField(loadout, "abilities", abilities ?? new AbilityDefinition[0]);
            return loadout;
        }

        private TimelineAbilityDefinition NewAbility(string id)
        {
            TimelineAbilityDefinition ability = ScriptableObject.CreateInstance<TimelineAbilityDefinition>();
            owned.Add(ability);
            SetField(ability, "abilityId", id);
            SetField(ability, "requiresTarget", false);
            return ability;
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
