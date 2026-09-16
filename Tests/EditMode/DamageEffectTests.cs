using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    /// <summary>
    /// Exercises DamageEffectDefinition in its area configuration through its
    /// public trigger path with real GameObjects, colliders and physics
    /// queries: both centerings, falloff, target limits, duplicate-hit
    /// rejection, and the attribute the damage lands on. What a hit does to the
    /// target beyond the number is <see cref="HitReactionTests"/>.
    /// </summary>
    public sealed class DamageEffectTests
    {
        private static readonly GameplayAttribute Health = new("Test.Health");

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
        public void AimPointCentering_HitsOnlyActorsInRadius()
        {
            GameObject owner = NewOwner();
            GameObject primary = NewTarget(new Vector3(2f, 0f, 0f));
            GameObject near = NewTarget(new Vector3(4f, 0f, 0f));
            GameObject far = NewTarget(new Vector3(10f, 0f, 0f));

            DamageEffectDefinition effect = NewEffect(AimSphere(5f), damage: 10f);

            Apply(owner, primary, effect);

            Assert.AreEqual(90f, HealthOf(primary), 1e-3f);
            Assert.AreEqual(90f, HealthOf(near), 1e-3f);
            Assert.AreEqual(100f, HealthOf(far), 1e-3f);
        }

        [Test]
        public void OwnerOffsetCentering_IgnoresAimPoint()
        {
            GameObject owner = NewOwner();
            GameObject atAim = NewTarget(new Vector3(8f, 0f, 0f));
            GameObject atOffset = NewTarget(new Vector3(0f, 0f, 3.5f));

            DamageEffectDefinition effect = NewEffect(
                HitShape.Sphere(new Vector3(0f, 0f, 3f), 2f)
                    .WithOrigin(HitShapeOrigin.OwnerLocal),
                damage: 10f);

            Apply(owner, atAim, effect);

            Assert.AreEqual(100f, HealthOf(atAim), 1e-3f);
            Assert.AreEqual(90f, HealthOf(atOffset), 1e-3f);
        }

        [Test]
        public void LinearFalloff_ScalesDamageByDistance()
        {
            GameObject owner = NewOwner();
            GameObject center = NewTarget(Vector3.zero);
            GameObject mid = NewTarget(new Vector3(2f, 0f, 0f));
            GameObject edge = NewTarget(new Vector3(2.5f, 0f, 0f));

            DamageEffectDefinition effect = NewEffect(
                AimSphere(4f), damage: 10f, falloff: true);

            Apply(owner, center, effect);

            Assert.AreEqual(90f, HealthOf(center), 1e-3f);
            Assert.AreEqual(95f, HealthOf(mid), 1e-3f);
            Assert.AreEqual(96f, HealthOf(edge), 1e-3f);
        }

        [Test]
        public void MaximumTargets_LimitsAcceptedHits()
        {
            GameObject owner = NewOwner();
            GameObject first = NewTarget(new Vector3(1f, 0f, 0f));
            GameObject second = NewTarget(new Vector3(-1f, 0f, 0f));

            DamageEffectDefinition effect = NewEffect(
                AimSphere(4f), damage: 10f, maximumTargets: 1);

            Apply(owner, first, effect);

            float dealt = 200f - HealthOf(first) - HealthOf(second);
            Assert.AreEqual(10f, dealt, 1e-3f);
        }

        [Test]
        public void DuplicateTrigger_RejectsSecondHitOnSameInstance()
        {
            GameObject owner = NewOwner();
            GameObject target = NewTarget(new Vector3(1f, 0f, 0f));

            DamageEffectDefinition effect = NewEffect(AimSphere(4f), damage: 10f);

            AbilitySystem system = owner.GetComponent<AbilitySystem>();
            TimelineAbilityDefinition definition =
                ScriptableObject.CreateInstance<TimelineAbilityDefinition>();
            owned.Add(definition);
            AbilityContext context = AbilityContext.FromTarget(owner, target);
            AbilityInstance instance = new(definition, context);
            GameplayEffectContext effectContext = new(system, instance, 0);

            Physics.SyncTransforms();
            effect.ApplyFrom(effectContext);
            effect.ApplyFrom(effectContext);

            Assert.AreEqual(90f, HealthOf(target), 1e-3f);
        }

        /// <summary>
        /// The damage number comes off the spec, so a source attribute scales it
        /// under the same capture rules every other magnitude uses.
        /// </summary>
        [Test]
        public void DamageScalesWithACapturedSourceAttribute()
        {
            GameplayAttribute strength = new("Test.Strength");
            GameObject owner = NewOwner();
            owner.AddComponent<AttributeSet>().SetInitialValues(new[]
            {
                new AttributeSet.InitialValue(strength, 6f, 0f, 100f),
            });
            GameObject target = NewTarget(new Vector3(1f, 0f, 0f));

            DamageEffectDefinition effect = NewEffect(AimSphere(4f), damage: 10f);
            effect.ConfigureDamageForTests(
                10f, Health, scaleAttribute: strength, scaleFactor: 2f);

            Apply(owner, target, effect);

            Assert.AreEqual(78f, HealthOf(target), 1e-3f);
        }

        /// <summary>
        /// A target that refuses the hit — invulnerable, dead, parrying — takes
        /// no damage and does not count against the effect target limit. The
        /// refusal is the target's: its container blocks the application and
        /// says so.
        /// </summary>
        [Test]
        public void RefusedHit_DoesNotConsumeATargetSlot()
        {
            GameObject owner = NewOwner();
            GameObject refusing = NewTarget(new Vector3(1f, 0f, 0f));
            refusing.AddComponent<AbilitySystem>().SetLooseTag(CommonGameplayTags.Invulnerable, true);
            int blocked = 0;
            GameplayEffectContainer.For(refusing).EffectBlocked += _ => blocked++;
            GameObject accepting = NewTarget(new Vector3(-1f, 0f, 0f));

            DamageEffectDefinition effect = NewEffect(
                AimSphere(4f), damage: 10f, maximumTargets: 1);

            Apply(owner, refusing, effect);

            Assert.AreEqual(1, blocked);
            Assert.AreEqual(100f, HealthOf(refusing), 1e-3f);
            Assert.AreEqual(90f, HealthOf(accepting), 1e-3f);
        }

        /// <summary>
        /// Damage lands on an attribute set, so a collider with none behind it
        /// is scenery to this effect: not hit, not counted, no slot spent.
        /// </summary>
        [Test]
        public void AnActorWithoutAttributes_IsNotADamageTarget()
        {
            GameObject owner = NewOwner();
            GameObject scenery = NewTargetObject(new Vector3(1f, 0f, 0f));
            GameObject actor = NewTarget(new Vector3(-1f, 0f, 0f));

            DamageEffectDefinition effect = NewEffect(
                AimSphere(4f), damage: 10f, maximumTargets: 1);

            int accepted = ApplyCounting(owner, scenery, effect);

            Assert.AreEqual(1, accepted);
            Assert.AreEqual(90f, HealthOf(actor), 1e-3f);
        }

        /// <summary>
        /// The attribute change names the attacker as its source, which is what
        /// a health readout, a kill credit or a death event reads.
        /// </summary>
        [Test]
        public void DamageNamesItsSource()
        {
            GameObject owner = NewOwner();
            GameObject target = NewTarget(new Vector3(1f, 0f, 0f));
            Object source = null;
            target.GetComponent<AttributeSet>().Changed += change => source = change.Source;

            Apply(owner, target, NewEffect(AimSphere(4f), damage: 10f));

            Assert.AreSame(owner, source);
        }

        private static HitShape AimSphere(float radius)
        {
            return HitShape.Sphere(Vector3.zero, radius)
                .WithOrigin(HitShapeOrigin.AbilityAimPoint);
        }

        private static float HealthOf(GameObject actor)
        {
            return actor.GetComponent<AttributeSet>().GetCurrent(Health);
        }

        private GameObject NewOwner()
        {
            GameObject owner = new("AoEOwner");
            owned.Add(owner);
            owner.AddComponent<AbilitySystem>();
            return owner;
        }

        private GameObject NewTarget(Vector3 position)
        {
            GameObject target = NewTargetObject(position);
            target.AddComponent<AttributeSet>().SetInitialValues(new[]
            {
                new AttributeSet.InitialValue(Health, 100f, 0f, 100f),
            });
            return target;
        }

        private GameObject NewTargetObject(Vector3 position)
        {
            GameObject target = new("AoETarget");
            owned.Add(target);
            target.transform.position = position;
            SphereCollider collider = target.AddComponent<SphereCollider>();
            collider.radius = 0.5f;
            collider.isTrigger = false;
            return target;
        }

        /// <summary>
        /// The area behaviour that used to be its own effect class is now a
        /// configuration of the single damage effect: a sphere that sweeps
        /// everyone in range and pushes them away from the centre.
        /// </summary>
        private DamageEffectDefinition NewEffect(
            HitShape shape,
            float damage,
            bool falloff = false,
            int maximumTargets = 8,
            float horizontal = 0f,
            float vertical = 0f)
        {
            DamageEffectDefinition effect =
                ScriptableObject.CreateInstance<DamageEffectDefinition>();
            owned.Add(effect);
            effect.SetTargetingForTests(
                GameplayEffectTargeting.FromShape(shape, maximumTargets, false));
            effect.ConfigureDamageForTests(
                damage,
                Health,
                falloff,
                horizontal,
                vertical);
            effect.SetKnockbackDirectionForTests(KnockbackDirection.AwayFromShapeCenter);
            return effect;
        }

        private void Apply(GameObject owner, GameObject target, DamageEffectDefinition effect)
        {
            ApplyCounting(owner, target, effect);
        }

        private int ApplyCounting(GameObject owner, GameObject target, DamageEffectDefinition effect)
        {
            Physics.SyncTransforms();
            AbilitySystem system = owner.GetComponent<AbilitySystem>();
            TimelineAbilityDefinition definition =
                ScriptableObject.CreateInstance<TimelineAbilityDefinition>();
            owned.Add(definition);
            AbilityContext context = AbilityContext.FromTarget(owner, target);
            AbilityInstance instance = new(definition, context);
            return effect.ApplyFrom(new GameplayEffectContext(system, instance, 0));
        }
    }
}
