using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Fofuxo.GameplayAbilitySystem.Editor;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    /// <summary>
    /// The Add button behind a missing section of the actor panel. The rule it
    /// pins: a slot offers the concrete types, never a base an actor would end
    /// up holding twice, and what it adds is an ordinary visible component.
    /// </summary>
    public sealed class AbilitySystemComponentMenuTests
    {
        /// <summary>
        /// Nested on purpose: a fixture's own subclass must be addable by hand
        /// here and invisible to the Inspector of a real project.
        /// </summary>
        private sealed class NestedAttributeSet : AttributeSet
        {
        }

        private abstract class AbstractAttributeSet : AttributeSet
        {
        }

        private readonly List<Object> owned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (Object item in owned)
            {
                if (item != null)
                {
                    Object.DestroyImmediate(item);
                }
            }

            owned.Clear();
        }

        [Test]
        public void ConcreteTypes_ListsSubclassesSortedByName_AndNeverTheSlotType()
        {
            List<System.Type> types = AbilitySystemComponentMenu.ConcreteTypes(
                typeof(Component),
                new[] { typeof(AbilitySystem), typeof(Component), typeof(AbilityPhysicsBody) });

            CollectionAssert.AreEqual(
                new[] { typeof(AbilityPhysicsBody), typeof(AbilitySystem) }, types);
        }

        [Test]
        public void ConcreteTypes_FallsBackToTheSlotType_WhenNothingElseIsAddable()
        {
            List<System.Type> types = AbilitySystemComponentMenu.ConcreteTypes(
                typeof(AttributeSet),
                new[] { typeof(NestedAttributeSet), typeof(AbstractAttributeSet) });

            CollectionAssert.AreEqual(new[] { typeof(AttributeSet) }, types);
        }

        [Test]
        public void ConcreteTypes_IsEmpty_WhenTheSlotItselfIsAbstract()
        {
            List<System.Type> types = AbilitySystemComponentMenu.ConcreteTypes(
                typeof(AbstractAttributeSet), System.Array.Empty<System.Type>());

            Assert.IsEmpty(types);
        }

        [Test]
        public void IsAddable_AcceptsOnlyConcreteFileScopedComponents()
        {
            Assert.IsTrue(AbilitySystemComponentMenu.IsAddable(typeof(AttributeSet)));
            Assert.IsTrue(AbilitySystemComponentMenu.IsAddable(typeof(AbilitySystem)));
            Assert.IsFalse(AbilitySystemComponentMenu.IsAddable(typeof(NestedAttributeSet)), "nested");
            Assert.IsFalse(AbilitySystemComponentMenu.IsAddable(typeof(AbstractAttributeSet)), "abstract");
            Assert.IsFalse(AbilitySystemComponentMenu.IsAddable(typeof(List<>)), "generic definition");
            Assert.IsFalse(AbilitySystemComponentMenu.IsAddable(typeof(string)), "not a component");
            Assert.IsFalse(AbilitySystemComponentMenu.IsAddable(null));
        }

        /// <summary>
        /// The Editor-fed list is what the Inspector shows. This fixture's
        /// subclass is compiled into the same domain and must not be in it.
        /// </summary>
        [Test]
        public void ConcreteTypes_FromTheEditor_HidesANestedTestSubclass()
        {
            List<System.Type> types = AbilitySystemComponentMenu.ConcreteTypes(typeof(AttributeSet));

            CollectionAssert.DoesNotContain(types, typeof(NestedAttributeSet));
            CollectionAssert.DoesNotContain(types, typeof(AbstractAttributeSet));
            Assert.IsNotEmpty(types, "at worst the base itself is offered");
        }

        [Test]
        public void Add_CreatesTheSubclass_AsTheOnlyAttributeSet_AndLeavesItVisible()
        {
            GameObject owner = Own(new GameObject("AddOwner"));
            owner.AddComponent<AbilitySystem>();

            Component added = AbilitySystemComponentMenu.Add(owner, typeof(NestedAttributeSet));

            Assert.IsInstanceOf<NestedAttributeSet>(added);
            Assert.AreEqual(1, owner.GetComponents<AttributeSet>().Length);
            Assert.AreSame(added, owner.GetComponent<AttributeSet>());
            Assert.AreEqual(HideFlags.None, added.hideFlags);
        }

        [Test]
        public void Add_RefusesWhatIsNotAComponent()
        {
            GameObject owner = Own(new GameObject("AddOwner"));

            Assert.IsNull(AbilitySystemComponentMenu.Add(owner, typeof(string)));
            Assert.IsNull(AbilitySystemComponentMenu.Add(null, typeof(AttributeSet)));
            Assert.IsNull(AbilitySystemComponentMenu.Add(owner, null));
        }

        private T Own<T>(T item) where T : Object
        {
            owned.Add(item);
            return item;
        }
    }
}
