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
            TimelineAbilityDefinition first = NewAbility("test.buffer.first");
            TimelineAbilityDefinition second = NewAbility("test.buffer.second");
            Grant(first, second);
            SetField(router, "bufferWindow", 30f);

            AbilityContext context = AbilityContext.FromTarget(owner, null);
            Assert.IsTrue(system.TryActivate(first, context));
            InvokeBinding(second);
            Assert.AreEqual(first, system.ActiveAbility);

            system.ForceCancelActiveAbility(CommonGameplayTags.CancelManual);
            InvokeUpdate();
            Assert.AreEqual(second, system.ActiveAbility);
        }

        [Test]
        public void ExpiredBuffer_IsDropped()
        {
            TimelineAbilityDefinition first = NewAbility("test.buffer.first");
            TimelineAbilityDefinition second = NewAbility("test.buffer.second");
            Grant(first, second);
            SetField(router, "bufferWindow", 0.05f);

            AbilityContext context = AbilityContext.FromTarget(owner, null);
            Assert.IsTrue(system.TryActivate(first, context));
            InvokeBinding(second);

            // Editor time does not advance mid-test; expire the buffer directly.
            SetField(router, "bufferExpiry", Time.time - 1f);
            system.ForceCancelActiveAbility(CommonGameplayTags.CancelManual);
            InvokeUpdate();
            Assert.IsNull(system.ActiveAbility);
        }

        private TimelineAbilityDefinition NewAbility(string id)
        {
            TimelineAbilityDefinition ability = ScriptableObject.CreateInstance<TimelineAbilityDefinition>();
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

        private void InvokeBinding(TimelineAbilityDefinition ability)
        {
            MethodInfo method = typeof(AbilityInputRouter).GetMethod(
                "TryActivateBinding",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method);
            method.Invoke(router, new object[] { ability });
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
