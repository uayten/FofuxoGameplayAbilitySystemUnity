using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Fofuxo.GameplayAbilitySystem;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    public sealed class TargetAssistTests
    {
        private sealed class StubReceiver : MonoBehaviour, IAbilityDamageReceiver
        {
            public bool Damageable = true;
            public bool IsDamageable => Damageable;
            public bool TryReceiveDamage(AbilityHitInfo hit) => Damageable;
        }

        [Test]
        public void AssistCannotNestAnotherAbility()
        {
            TargetAssistDefinition outer =
                ScriptableObject.CreateInstance<TargetAssistDefinition>();
            TargetAssistDefinition inner =
                ScriptableObject.CreateInstance<TargetAssistDefinition>();
            try
            {
                SetField<AbilityDefinition, string>(outer, "abilityId", "test.assist.outer");
                SetField<AbilityDefinition, string>(inner, "abilityId", "test.assist.inner");
                outer.SetNestedAssistForTests(inner);
                Assert.IsFalse(outer.TryValidate(out string error));
                Assert.IsTrue(error.Contains("nest"));
            }
            finally
            {
                Object.DestroyImmediate(outer);
                Object.DestroyImmediate(inner);
            }
        }

        [Test]
        public void ActivationSnapsOwnerTowardDamageableTarget()
        {
            GameObject owner = new("AssistOwner");
            GameObject target = new("AssistTarget");
            AbilityDefinition attack = ScriptableObject.CreateInstance<AbilityDefinition>();
            TargetAssistDefinition assist = ScriptableObject.CreateInstance<TargetAssistDefinition>();
            AbilityLoadout loadout = ScriptableObject.CreateInstance<AbilityLoadout>();
            try
            {
                target.transform.position = new Vector3(2f, 0f, 3f);
                target.AddComponent<BoxCollider>();
                target.AddComponent<StubReceiver>();

                SetField(attack, "abilityId", "test.assist.attack");
                SetField(attack, "requiresTarget", false);
                SetField<AbilityDefinition, string>(assist, "abilityId", "test.assist");
                SetField(assist, "targetLayers", MakeMask(1 << target.layer));
                SetField(assist, "searchDistance", 5f);
                SetField(assist, "coneHalfAngle", 90f);
                SetField(assist, "proximityRadius", 0f);
                attack.SetNestedAssistForTests(assist);
                SetField(loadout, "abilities", new[] { attack });

                AbilitySystem system = owner.AddComponent<AbilitySystem>();
                SetField(system, "loadout", loadout);
                Physics.SyncTransforms();

                Assert.IsTrue(system.TryActivate(
                    attack, AbilityContext.FromTarget(owner, null)));

                Vector3 expected = (target.transform.position - owner.transform.position).normalized;
                expected.y = 0f;
                Assert.Less(Vector3.Angle(owner.transform.forward, expected), 3f);
                Assert.AreEqual(target, system.ActiveContext?.Target);
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(attack);
                Object.DestroyImmediate(assist);
                Object.DestroyImmediate(loadout);
            }
        }

        [Test]
        public void ActivationWithoutTargetKeepsFacing()
        {
            GameObject owner = new("AssistOwner");
            AbilityDefinition attack = ScriptableObject.CreateInstance<AbilityDefinition>();
            TargetAssistDefinition assist = ScriptableObject.CreateInstance<TargetAssistDefinition>();
            AbilityLoadout loadout = ScriptableObject.CreateInstance<AbilityLoadout>();
            try
            {
                SetField(attack, "abilityId", "test.assist.attack");
                SetField(attack, "requiresTarget", false);
                SetField<AbilityDefinition, string>(assist, "abilityId", "test.assist");
                SetField(assist, "targetLayers", MakeMask(-1));
                SetField(assist, "searchDistance", 5f);
                attack.SetNestedAssistForTests(assist);
                SetField(loadout, "abilities", new[] { attack });

                AbilitySystem system = owner.AddComponent<AbilitySystem>();
                SetField(system, "loadout", loadout);
                Vector3 before = owner.transform.forward;

                Assert.IsTrue(system.TryActivate(
                    attack, AbilityContext.FromTarget(owner, null)));
                Assert.AreEqual(before, owner.transform.forward);
                Assert.IsNull(system.ActiveContext?.Target);
                Assert.IsFalse(system.HasActiveDisplacement);
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(attack);
                Object.DestroyImmediate(assist);
                Object.DestroyImmediate(loadout);
            }
        }

        [Test]
        public void ZeroSearchDistanceUsesTwiceProximityRadius()
        {
            TargetAssistDefinition assist =
                ScriptableObject.CreateInstance<TargetAssistDefinition>();
            try
            {
                SetField(assist, "searchDistance", 0f);
                SetField(assist, "proximityRadius", 4f);

                Assert.AreEqual(8f, assist.ResolveSearchDistance());
            }
            finally
            {
                Object.DestroyImmediate(assist);
            }
        }

        [Test]
        public void ProximityRadiusSelectsTargetOutsideTheForwardCone()
        {
            GameObject owner = new("AssistOwner");
            GameObject target = new("AssistTarget");
            AbilityDefinition attack = ScriptableObject.CreateInstance<AbilityDefinition>();
            TargetAssistDefinition assist = ScriptableObject.CreateInstance<TargetAssistDefinition>();
            AbilityLoadout loadout = ScriptableObject.CreateInstance<AbilityLoadout>();
            try
            {
                target.transform.position = Vector3.back * 3f;
                target.AddComponent<BoxCollider>();
                target.AddComponent<StubReceiver>();

                SetField(attack, "abilityId", "test.assist.attack");
                SetField(attack, "requiresTarget", false);
                SetField<AbilityDefinition, string>(assist, "abilityId", "test.assist");
                SetField(assist, "targetLayers", MakeMask(1 << target.layer));
                SetField(assist, "searchDistance", 0f);
                SetField(assist, "proximityRadius", 4f);
                SetField(assist, "coneHalfAngle", 35f);
                attack.SetNestedAssistForTests(assist);
                SetField(loadout, "abilities", new[] { attack });

                AbilitySystem system = owner.AddComponent<AbilitySystem>();
                SetField(system, "loadout", loadout);
                Physics.SyncTransforms();

                Assert.IsTrue(system.TryActivate(
                    attack, AbilityContext.FromDirection(owner, null, Vector3.forward)));
                Assert.AreEqual(target, system.ActiveContext?.Target);
                Assert.Less(Vector3.Angle(owner.transform.forward, Vector3.back), 3f);
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(attack);
                Object.DestroyImmediate(assist);
                Object.DestroyImmediate(loadout);
            }
        }

        [Test]
        public void SelectedDistantTargetStartsApproachDuringParentStartup()
        {
            GameObject owner = new("AssistOwner");
            GameObject target = new("AssistTarget");
            AbilityDefinition attack = ScriptableObject.CreateInstance<AbilityDefinition>();
            TargetAssistDefinition assist = ScriptableObject.CreateInstance<TargetAssistDefinition>();
            AbilityLoadout loadout = ScriptableObject.CreateInstance<AbilityLoadout>();
            try
            {
                owner.AddComponent<Rigidbody>();
                target.transform.position = Vector3.forward * 7f;
                target.AddComponent<BoxCollider>();
                target.AddComponent<StubReceiver>();

                SetField(attack, "abilityId", "test.assist.attack");
                SetField(attack, "requiresTarget", false);
                SetField(attack, "startupEndFrame", 20);
                SetField(attack, "activeEndFrame", 21);
                SetField<AbilityDefinition, string>(assist, "abilityId", "test.assist");
                SetField(assist, "targetLayers", MakeMask(1 << target.layer));
                SetField(assist, "searchDistance", 0f);
                SetField(assist, "proximityRadius", 4f);
                SetField(assist, "approachTarget", true);
                SetField(assist, "stoppingDistance", 0f);
                attack.SetNestedAssistForTests(assist);
                SetField(loadout, "abilities", new[] { attack });

                AbilitySystem system = owner.AddComponent<AbilitySystem>();
                SetField(system, "loadout", loadout);
                Physics.SyncTransforms();

                Assert.IsTrue(system.TryActivate(
                    attack, AbilityContext.FromDirection(owner, null, Vector3.forward)));
                Assert.AreEqual(target, system.ActiveContext?.Target);
                Assert.IsTrue(system.HasActiveDisplacement);
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(attack);
                Object.DestroyImmediate(assist);
                Object.DestroyImmediate(loadout);
            }
        }

        [Test]
        public void ApproachAndParentDisplacementCannotShareOneAbility()
        {
            AbilityDefinition attack = ScriptableObject.CreateInstance<AbilityDefinition>();
            TargetAssistDefinition assist = ScriptableObject.CreateInstance<TargetAssistDefinition>();
            try
            {
                SetField(attack, "abilityId", "test.assist.attack");
                SetField<AbilityDefinition, string>(assist, "abilityId", "test.assist");
                SetField(assist, "approachTarget", true);
                attack.SetNestedAssistForTests(assist);
                attack.ConfigureDisplacementForTests(
                    AbilityDisplacementDirection.Context,
                    1f,
                    1,
                    2);

                Assert.IsFalse(attack.TryValidate(out string error));
                StringAssert.Contains("displacement", error);
            }
            finally
            {
                Object.DestroyImmediate(attack);
                Object.DestroyImmediate(assist);
            }
        }

        private static LayerMask MakeMask(int bits)
        {
            LayerMask mask = default;
            mask.value = bits;
            return mask;
        }

        private static void SetField<TTarget, TValue>(
            TTarget target,
            string fieldName,
            TValue value)
        {
            FieldInfo field = typeof(TTarget).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, fieldName);
            field.SetValue(target, value);
        }
    }
}
