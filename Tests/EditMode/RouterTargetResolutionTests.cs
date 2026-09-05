using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Fofuxo.GameplayAbilitySystem;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    public sealed class RouterTargetResolutionTests
    {
        private GameObject owner;
        private GameObject farTarget;
        private AbilitySystem system;
        private AbilityInputRouter router;
        private readonly List<Object> owned = new();

        [SetUp]
        public void SetUp()
        {
            owner = new GameObject("RouterOwner");
            system = owner.AddComponent<AbilitySystem>();
            router = owner.AddComponent<AbilityInputRouter>();
            // Awake timing in EditMode is not guaranteed; wire explicitly.
            SetField(router, "abilitySystem", system);

            farTarget = new GameObject("FarTarget");
            farTarget.transform.position = new Vector3(10f, 0f, 0f);
            AbilityInputRouter.GlobalFallbackTargetResolver = () => farTarget;
        }

        [TearDown]
        public void TearDown()
        {
            AbilityInputRouter.GlobalFallbackTargetResolver = null;
            foreach (Object ownedObject in owned)
            {
                if (ownedObject != null)
                {
                    Object.DestroyImmediate(ownedObject);
                }
            }

            owned.Clear();
            Object.DestroyImmediate(farTarget);
            Object.DestroyImmediate(owner);
        }

        [Test]
        public void TargetlessAbility_IgnoresFallbackTarget()
        {
            AbilityDefinition nova = NewAbility("test.nova", requiresTarget: false);
            Grant(nova);

            InvokeBinding(nova, null);

            // A fallback target 10m away with range 3 would refuse activation
            // if it were resolved; targetless abilities skip resolution.
            Assert.AreEqual(nova, system.ActiveAbility);
        }

        [Test]
        public void TargetedAbility_StillValidatesAgainstFallbackTarget()
        {
            AbilityDefinition strike = NewAbility("test.strike", requiresTarget: true);
            Grant(strike);

            InvokeBinding(strike, null);

            Assert.IsNull(system.ActiveAbility);
        }

        private AbilityDefinition NewAbility(string id, bool requiresTarget)
        {
            AbilityDefinition ability = ScriptableObject.CreateInstance<AbilityDefinition>();
            owned.Add(ability);
            SetField(ability, "abilityId", id);
            SetField(ability, "requiresTarget", requiresTarget);
            SetField(ability, "maximumRange", 3f);
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
