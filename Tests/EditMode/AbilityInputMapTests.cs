using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    /// <summary>
    /// The map names actions instead of referencing them, and this is the price
    /// of that: the name has to be checked against the asset, loudly. A binding
    /// pointing at an action that was renamed is the failure a string mapping is
    /// usually accused of hiding, so it is the one pinned first.
    /// </summary>
    public sealed class AbilityInputMapTests
    {
        private InputActionAsset actions;
        private AbilityInputMap map;
        private AbilityDefinition ability;

        [SetUp]
        public void SetUp()
        {
            actions = ScriptableObject.CreateInstance<InputActionAsset>();
            InputActionMap player = actions.AddActionMap("Player");
            player.AddAction("Attack");
            player.AddAction("Roll");

            map = ScriptableObject.CreateInstance<AbilityInputMap>();
            ability = ScriptableObject.CreateInstance<AbilityDefinition>();
            ability.SetAbilityIdForTests("test.input.ability");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(map);
            Object.DestroyImmediate(actions);
            Object.DestroyImmediate(ability);
        }

        [Test]
        public void AMapOfKnownActionsIsValid()
        {
            map.ConfigureForTests(
                actions,
                new AbilityInputEntry("Attack", ability),
                new AbilityInputEntry("Roll", null));

            Assert.IsTrue(map.TryValidate(out string error), error);
        }

        /// <summary>
        /// An entry with no ability is not a mistake: the router still owns the
        /// keybind and forwards it, which is how a combat input that is not an
        /// ability stays in the same asset as the ones that are.
        /// </summary>
        [Test]
        public void AnEntryWithoutAnAbilityIsForwarded()
        {
            map.ConfigureForTests(actions, new AbilityInputEntry("Roll", null));

            Assert.IsTrue(map.Entries[0].IsForwarded);
            Assert.IsTrue(map.TryValidate(out _));
        }

        [Test]
        public void AnActionMissingFromTheAssetFailsValidation()
        {
            map.ConfigureForTests(actions, new AbilityInputEntry("Parry", ability));

            Assert.IsFalse(map.TryValidate(out string error));
            StringAssert.Contains("Parry", error);
        }

        [Test]
        public void AnEmptyActionNameFailsValidation()
        {
            map.ConfigureForTests(actions, new AbilityInputEntry(string.Empty, ability));

            Assert.IsFalse(map.TryValidate(out string error));
            StringAssert.Contains("no action", error);
        }

        [Test]
        public void TheSameActionBoundTwiceFailsValidation()
        {
            map.ConfigureForTests(
                actions,
                new AbilityInputEntry("Attack", ability),
                new AbilityInputEntry("Attack", null));

            Assert.IsFalse(map.TryValidate(out string error));
            StringAssert.Contains("bound twice", error);
        }

        [Test]
        public void WithoutAnAssetNoNameCanResolve()
        {
            map.ConfigureForTests(null, new AbilityInputEntry("Attack", ability));

            Assert.IsFalse(map.TryValidate(out string error));
            StringAssert.Contains("Input Action Asset", error);
        }

        [Test]
        public void FindActionResolvesAgainstTheAsset()
        {
            map.ConfigureForTests(actions, new AbilityInputEntry("Attack", ability));

            Assert.IsNotNull(map.FindAction("Attack"));
            Assert.IsNull(map.FindAction("Parry"));
        }
    }
}
