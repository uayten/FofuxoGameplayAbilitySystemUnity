using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Fofuxo.GameplayAbilitySystem;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    public sealed class InputBufferTests
    {
        private GameObject owner;
        private AbilitySystem system;
        private AbilityInputRouter router;
        private readonly List<Object> owned = new();

        [SetUp]
        public void SetUp()
        {
            owner = new GameObject("BufferOwner");
            system = owner.AddComponent<AbilitySystem>();
            router = owner.AddComponent<AbilityInputRouter>();
            // Awake timing in EditMode is not guaranteed; wire explicitly.
            SetField(router, "abilitySystem", system);
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
        public void RejectedInput_IsRetriedUntilItFits()
        {
            AbilityDefinition first = NewAbility("test.buffer.first");
            AbilityDefinition second = NewAbility("test.buffer.second");
            Grant(first, second);
            SetField(router, "bufferWindow", 30f);

            AbilityContext context = AbilityContext.FromTarget(owner, null);
            Assert.IsTrue(system.TryActivate(first, context));
            InvokeBinding(second, null);
            Assert.AreEqual(first, system.ActiveAbility);

            system.ForceCancelActiveAbility(AbilityCancelReason.Manual);
            InvokeUpdate();
            Assert.AreEqual(second, system.ActiveAbility);
        }

        [Test]
        public void ExpiredBuffer_IsDropped()
        {
            AbilityDefinition first = NewAbility("test.buffer.first");
            AbilityDefinition second = NewAbility("test.buffer.second");
            Grant(first, second);
            SetField(router, "bufferWindow", 0.05f);

            AbilityContext context = AbilityContext.FromTarget(owner, null);
            Assert.IsTrue(system.TryActivate(first, context));
            InvokeBinding(second, null);

            // Editor time does not advance mid-test; expire the buffer directly.
            SetField(router, "bufferExpiry", Time.time - 1f);
            system.ForceCancelActiveAbility(AbilityCancelReason.Manual);
            InvokeUpdate();
            Assert.IsNull(system.ActiveAbility);
        }

        private AbilityDefinition NewAbility(string id)
        {
            AbilityDefinition ability = ScriptableObject.CreateInstance<AbilityDefinition>();
            owned.Add(ability);
            SetField(ability, "abilityId", id);
            SetField(ability, "requiresTarget", false);
            return ability;
        }

        private void Grant(params AbilityDefinition[] abilities)
        {
            AbilityLoadout loadout = ScriptableObject.CreateInstance<AbilityLoadout>();
            owned.Add(loadout);
            SetField(loadout, "abilities", abilities);
            SetField(system, "loadout", loadout);
        }

        private void InvokeBinding(AbilityDefinition ability, AbilitySequenceDefinition sequence)
        {
            MethodInfo method = typeof(AbilityInputRouter).GetMethod(
                "TryActivateBinding",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method);
            method.Invoke(router, new object[] { ability, sequence });
        }

        private void InvokeUpdate()
        {
            MethodInfo method = typeof(AbilityInputRouter).GetMethod(
                "Update",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method);
            method.Invoke(router, null);
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
