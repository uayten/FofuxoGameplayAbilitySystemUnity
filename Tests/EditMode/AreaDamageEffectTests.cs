using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Fofuxo.GameplayAbilitySystem;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    /// <summary>
    /// Exercises AreaDamageEffectDefinition through its public trigger path
    /// with real GameObjects, colliders, physics queries, and receivers:
    /// both centerings, falloff, radial knockback, target limits, and
    /// duplicate-hit rejection.
    /// </summary>
    public sealed class AreaDamageEffectTests
    {
        private sealed class TestReceiver : MonoBehaviour, IAbilityDamageReceiver
        {
            public readonly List<AbilityHitInfo> Hits = new();
            public bool IsDamageable => true;

            public bool TryReceiveDamage(AbilityHitInfo hit)
            {
                Hits.Add(hit);
                return true;
            }
        }

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
        public void AimPointCentering_HitsOnlyReceiversInRadius()
        {
            GameObject owner = NewOwner();
            TestReceiver primary = NewTarget(new Vector3(2f, 0f, 0f));
            TestReceiver near = NewTarget(new Vector3(4f, 0f, 0f));
            TestReceiver far = NewTarget(new Vector3(10f, 0f, 0f));

            AreaDamageEffectDefinition effect = NewEffect();
            SetEffect(effect, "centerMode", 0);
            SetEffect(effect, "radius", 5f);
            SetEffect(effect, "damage", 10);
            SetEffect(effect, "linearFalloff", false);
            SetEffect(effect, "maximumTargets", 8);

            Apply(owner, primary.gameObject, effect);

            Assert.AreEqual(1, primary.Hits.Count);
            Assert.AreEqual(10, primary.Hits[0].Amount);
            Assert.AreEqual(1, near.Hits.Count);
            Assert.AreEqual(10, near.Hits[0].Amount);
            Assert.AreEqual(0, far.Hits.Count);
        }

        [Test]
        public void OwnerOffsetCentering_IgnoresAimPoint()
        {
            GameObject owner = NewOwner();
            TestReceiver atAim = NewTarget(new Vector3(8f, 0f, 0f));
            TestReceiver atOffset = NewTarget(new Vector3(0f, 0f, 3.5f));

            AreaDamageEffectDefinition effect = NewEffect();
            SetEffect(effect, "centerMode", 1);
            SetEffect(effect, "localCenter", new Vector3(0f, 0f, 3f));
            SetEffect(effect, "radius", 2f);
            SetEffect(effect, "damage", 10);
            SetEffect(effect, "linearFalloff", false);
            SetEffect(effect, "maximumTargets", 8);

            Apply(owner, atAim.gameObject, effect);

            Assert.AreEqual(0, atAim.Hits.Count);
            Assert.AreEqual(1, atOffset.Hits.Count);
        }

        [Test]
        public void LinearFalloff_ScalesDamageByDistance()
        {
            GameObject owner = NewOwner();
            TestReceiver center = NewTarget(Vector3.zero);
            TestReceiver mid = NewTarget(new Vector3(2f, 0f, 0f));
            TestReceiver edge = NewTarget(new Vector3(2.5f, 0f, 0f));

            AreaDamageEffectDefinition effect = NewEffect();
            SetEffect(effect, "centerMode", 0);
            SetEffect(effect, "radius", 4f);
            SetEffect(effect, "damage", 10);
            SetEffect(effect, "linearFalloff", true);
            SetEffect(effect, "maximumTargets", 8);

            Apply(owner, center.gameObject, effect);

            Assert.AreEqual(10, center.Hits[0].Amount);
            Assert.AreEqual(5, mid.Hits[0].Amount);
            Assert.AreEqual(4, edge.Hits[0].Amount);
        }

        [Test]
        public void Knockback_PushesRadiallyFromCenter()
        {
            GameObject owner = NewOwner();
            TestReceiver target = NewTarget(new Vector3(1.5f, 0f, 0f));

            AreaDamageEffectDefinition effect = NewEffect();
            SetEffect(effect, "centerMode", 0);
            SetEffect(effect, "radius", 4f);
            SetEffect(effect, "damage", 10);
            SetEffect(effect, "linearFalloff", false);
            SetEffect(effect, "radialKnockback", 6f);
            SetEffect(effect, "verticalKnockback", 2f);
            SetEffect(effect, "maximumTargets", 8);

            // Aim at empty ground so the receiver sits off-center.
            GameObject aim = new("AoEAim");
            owned.Add(aim);
            aim.transform.position = Vector3.zero;
            Apply(owner, aim, effect);

            Assert.AreEqual(1, target.Hits.Count);
            Vector3 knockback = target.Hits[0].Knockback;
            Assert.AreEqual(6f, knockback.x, 0.0001f);
            Assert.AreEqual(2f, knockback.y, 0.0001f);
            Assert.AreEqual(0f, knockback.z, 0.0001f);
        }

        [Test]
        public void MaximumTargets_LimitsAcceptedHits()
        {
            GameObject owner = NewOwner();
            TestReceiver first = NewTarget(new Vector3(1f, 0f, 0f));
            TestReceiver second = NewTarget(new Vector3(-1f, 0f, 0f));

            AreaDamageEffectDefinition effect = NewEffect();
            SetEffect(effect, "centerMode", 0);
            SetEffect(effect, "radius", 4f);
            SetEffect(effect, "damage", 10);
            SetEffect(effect, "linearFalloff", false);
            SetEffect(effect, "maximumTargets", 1);

            Apply(owner, first.gameObject, effect);

            Assert.AreEqual(1, first.Hits.Count + second.Hits.Count);
        }

        [Test]
        public void DuplicateTrigger_RejectsSecondHitOnSameInstance()
        {
            GameObject owner = NewOwner();
            TestReceiver target = NewTarget(new Vector3(1f, 0f, 0f));

            AreaDamageEffectDefinition effect = NewEffect();
            SetEffect(effect, "centerMode", 0);
            SetEffect(effect, "radius", 4f);
            SetEffect(effect, "damage", 10);
            SetEffect(effect, "linearFalloff", false);
            SetEffect(effect, "maximumTargets", 8);

            AbilitySystem system = owner.GetComponent<AbilitySystem>();
            AbilityDefinition definition = ScriptableObject.CreateInstance<AbilityDefinition>();
            owned.Add(definition);
            AbilityContext context = AbilityContext.FromTarget(owner, target.gameObject);
            AbilityInstance instance = new(definition, context);
            AbilityEffectContext effectContext = new(system, instance, 0);

            effect.Apply(effectContext);
            effect.Apply(effectContext);

            Assert.AreEqual(1, target.Hits.Count);
        }

        private GameObject NewOwner()
        {
            GameObject owner = new("AoEOwner");
            owned.Add(owner);
            owner.AddComponent<AbilitySystem>();
            return owner;
        }

        private TestReceiver NewTarget(Vector3 position)
        {
            GameObject target = new("AoETarget");
            owned.Add(target);
            target.transform.position = position;
            SphereCollider collider = target.AddComponent<SphereCollider>();
            collider.radius = 0.5f;
            collider.isTrigger = false;
            return target.AddComponent<TestReceiver>();
        }

        private AreaDamageEffectDefinition NewEffect()
        {
            AreaDamageEffectDefinition effect =
                ScriptableObject.CreateInstance<AreaDamageEffectDefinition>();
            owned.Add(effect);
            return effect;
        }

        private void Apply(GameObject owner, GameObject target, AreaDamageEffectDefinition effect)
        {
            Physics.SyncTransforms();
            AbilitySystem system = owner.GetComponent<AbilitySystem>();
            AbilityDefinition definition = ScriptableObject.CreateInstance<AbilityDefinition>();
            owned.Add(definition);
            AbilityContext context = AbilityContext.FromTarget(owner, target);
            AbilityInstance instance = new(definition, context);
            effect.Apply(new AbilityEffectContext(system, instance, 0));
        }

        private static void SetEffect<TValue>(
            AreaDamageEffectDefinition effect, string fieldName, TValue value)
        {
            var field = typeof(AreaDamageEffectDefinition).GetField(
                fieldName,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(field, fieldName);
            object boxedValue = value;
            if (field.FieldType.IsEnum)
            {
                boxedValue = System.Enum.ToObject(field.FieldType, value);
            }

            field.SetValue(effect, boxedValue);
        }
    }
}
