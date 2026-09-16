using NUnit.Framework;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    /// <summary>
    /// Effect validation catches the mistakes that produce silence instead of
    /// an error: a modifier pointing at nothing, an attribute folded in and
    /// then multiplied by zero, a duration effect with no duration, a query
    /// shape with no layers. Every one of these used to author cleanly, save
    /// cleanly, and do nothing at run time.
    ///
    /// The chaining tests are the load-bearing half: an effect is normally
    /// reached through the step that fires it, so the ability has to report it.
    /// </summary>
    public sealed class GameplayEffectValidationTests : GameplayEffectTestBase
    {
        private static readonly GameplayAttribute Poise = new("Test.Poise");
        private static readonly int EnemyLayerMask = 1 << 9;

        [Test]
        public void APlainInstantEffectIsValid()
        {
            GameplayEffectDefinition effect = NewEffect(
                GameplayEffectDurationPolicy.Instant, new[] { Add(Health, -10f) });

            Assert.IsTrue(effect.TryValidate(out string error), error);
        }

        [Test]
        public void AModifierWithNoAttributeIsRejectedByItsIndex()
        {
            GameplayEffectDefinition effect = NewEffect(
                GameplayEffectDurationPolicy.Instant,
                new[] { Add(Health, -10f), Add(default, -5f) });

            Assert.IsFalse(effect.TryValidate(out string error));
            StringAssert.Contains("Modifier 2", error);
        }

        /// <summary>
        /// The attribute is authored, the coefficient is left at zero, and
        /// <c>CapturesAttribute</c> is therefore false: the effect reads a flat
        /// number and the attribute in the Inspector is decoration.
        /// </summary>
        [Test]
        public void AScalingAttributeWithNoCoefficientIsRejected()
        {
            GameplayEffectDefinition effect = NewEffect(GameplayEffectDurationPolicy.Instant);
            effect.SetModifiersForTests(new GameplayEffectModifier(
                Health,
                AttributeOperation.Add,
                new GameplayEffectMagnitude(-10f, Strength, 0f)));

            Assert.IsFalse(effect.TryValidate(out string error));
            StringAssert.Contains("coefficient of zero", error);
        }

        [Test]
        public void AScalingAttributeWithACoefficientIsFine()
        {
            GameplayEffectDefinition effect = NewEffect(GameplayEffectDurationPolicy.Instant);
            effect.SetModifiersForTests(new GameplayEffectModifier(
                Health,
                AttributeOperation.Add,
                new GameplayEffectMagnitude(-10f, Strength, 0.5f)));

            Assert.IsTrue(effect.TryValidate(out string error), error);
        }

        [Test]
        public void ADurationEffectWithNoDurationIsRejected()
        {
            GameplayEffectDefinition effect = NewEffect(
                GameplayEffectDurationPolicy.Duration,
                new[] { Add(Poise, -5f) },
                duration: 0f);

            Assert.IsFalse(effect.TryValidate(out string error));
            StringAssert.Contains("Duration effect", error);
        }

        [Test]
        public void ADurationEffectThatReadsItsDurationFromAnAttributeIsFine()
        {
            GameplayEffectDefinition effect = NewEffect(
                GameplayEffectDurationPolicy.Duration, new[] { Add(Poise, -5f) });
            effect.SetDurationMagnitudeForTests(
                new GameplayEffectMagnitude(0f, Strength, 0.1f));

            Assert.IsTrue(effect.TryValidate(out string error), error);
        }

        [Test]
        public void AnInstantEffectWithAPeriodIsRejected()
        {
            GameplayEffectDefinition effect = NewEffect(
                GameplayEffectDurationPolicy.Instant,
                new[] { Add(Health, -1f) },
                period: 0.5f);

            Assert.IsFalse(effect.TryValidate(out string error));
            StringAssert.Contains("Instant effect", error);
        }

        [Test]
        public void AShapeWithNoTargetLayersIsRejected()
        {
            GameplayEffectDefinition effect = NewEffect(GameplayEffectDurationPolicy.Instant);
            effect.SetTargetingForTests(
                GameplayEffectTargeting.FromShape(HitShape.Sphere(Vector3.forward, 1f)));

            Assert.IsFalse(effect.TryValidate(out string error));
            StringAssert.Contains("Target Layers", error);
        }

        [Test]
        public void AShapeWithTargetLayersIsFine()
        {
            GameplayEffectDefinition effect = NewEffect(GameplayEffectDurationPolicy.Instant);
            effect.SetTargetingForTests(GameplayEffectTargeting.FromShape(
                HitShape.Sphere(Vector3.forward, 1f).WithLayers(EnemyLayerMask)));

            Assert.IsTrue(effect.TryValidate(out string error), error);
        }

        /// <summary>
        /// A shape carried for drawing rather than querying is never handed to
        /// the physics layer, so an empty mask there is not a mistake.
        /// </summary>
        [Test]
        public void AShapeCarriedOutsideShapeModeIsNotLayerChecked()
        {
            GameplayEffectDefinition effect = NewEffect(GameplayEffectDurationPolicy.Instant);
            effect.SetTargetingForTests(
                GameplayEffectTargeting.Owner.WithShape(HitShape.Sphere(Vector3.zero, 1f)));

            Assert.IsTrue(effect.TryValidate(out string error), error);
        }

        [Test]
        public void AnEmptyTagInAListIsRejectedByItsIndex()
        {
            GameplayEffectDefinition effect = NewEffect(GameplayEffectDurationPolicy.Instant);
            effect.SetTagsForTests(
                granted: new[] { new GameplayTag("State.Slowed"), default });

            Assert.IsFalse(effect.TryValidate(out string error));
            StringAssert.Contains("Granted tag 2", error);
        }

        [Test]
        public void AnEmptyOverflowSlotIsRejected()
        {
            GameplayEffectDefinition effect = NewEffect(GameplayEffectDurationPolicy.Duration,
                new[] { Add(Poise, -1f) }, duration: 1f);
            effect.SetStackingForTests(GameplayEffectStacking
                .With(EffectStacking.Stack, limit: 3)
                .WithOverflow(new GameplayEffectDefinition[] { null }));

            Assert.IsFalse(effect.TryValidate(out string error));
            StringAssert.Contains("Overflow effect 1", error);
        }

        [Test]
        public void AnEffectThatOverflowsIntoItselfIsRejected()
        {
            GameplayEffectDefinition effect = NewEffect(GameplayEffectDurationPolicy.Duration,
                new[] { Add(Poise, -1f) }, duration: 1f);
            effect.SetStackingForTests(GameplayEffectStacking
                .With(EffectStacking.Stack, limit: 3)
                .WithOverflow(new[] { effect }));

            Assert.IsFalse(effect.TryValidate(out string error));
            StringAssert.Contains("without end", error);
        }

        [Test]
        public void OverflowEffectsWithNoStackLimitAreRejected()
        {
            GameplayEffectDefinition overflow = NewEffect(GameplayEffectDurationPolicy.Instant);
            GameplayEffectDefinition effect = NewEffect(GameplayEffectDurationPolicy.Duration,
                new[] { Add(Poise, -1f) }, duration: 1f);
            effect.SetStackingForTests(GameplayEffectStacking
                .With(EffectStacking.Stack)
                .WithOverflow(new[] { overflow }));

            Assert.IsFalse(effect.TryValidate(out string error));
            StringAssert.Contains("stack limit is zero", error);
        }

        // ------------------------------------------------------- the subclasses

        [Test]
        public void DamageOfZeroIsRejected()
        {
            DamageEffectDefinition damage = NewDamage();
            damage.ConfigureDamageForTests(0f, Health);

            Assert.IsFalse(damage.TryValidate(out string error));
            StringAssert.Contains("Damage must be greater than zero", error);
        }

        /// <summary>
        /// Damage is an attribute change now, so an effect that names no
        /// attribute would subtract from nothing and still count as a hit.
        /// </summary>
        [Test]
        public void DamageWithoutATargetAttributeIsRejected()
        {
            DamageEffectDefinition damage = NewDamage();
            damage.ConfigureDamageForTests(10f);

            Assert.IsFalse(damage.TryValidate(out string error));
            StringAssert.Contains("Target Attribute", error);
        }

        [Test]
        public void DamageScaledByAnAttributeMayHaveNoFlatPart()
        {
            DamageEffectDefinition damage = NewDamage();
            damage.ConfigureDamageForTests(
                0f, Health, scaleAttribute: Strength, scaleFactor: 2f);

            Assert.IsTrue(damage.TryValidate(out string error), error);
        }

        [Test]
        public void FalloffOutsideShapeTargetingIsRejected()
        {
            DamageEffectDefinition damage = NewDamage();
            damage.SetTargetingForTests(GameplayEffectTargeting.AbilityTarget);
            damage.ConfigureDamageForTests(10f, Health, falloff: true);

            Assert.IsFalse(damage.TryValidate(out string error));
            StringAssert.Contains("falloff", error);
        }

        [Test]
        public void AKnockbackDurationWithNoKnockbackIsRejected()
        {
            DamageEffectDefinition damage = NewDamage();
            damage.ConfigureDamageForTests(10f, Health, horizontal: 0f, vertical: 0f, duration: 0.4f);

            Assert.IsFalse(damage.TryValidate(out string error));
            StringAssert.Contains("pushed nowhere", error);
        }

        [Test]
        public void APhysicsForceWithNoEventTagIsRejected()
        {
            PhysicsForceEffectDefinition force =
                Own(ScriptableObject.CreateInstance<PhysicsForceEffectDefinition>());
            force.name = "GE_Test_Force";
            force.ConfigureForTests(
                HitShape.Sphere(Vector3.forward, 1f).WithLayers(EnemyLayerMask),
                PhysicsForceDirection.OwnerForward,
                AnimationCurve.Constant(1f, 100f, 10f),
                AnimationCurve.Constant(1f, 100f, 4f));
            force.SetEventTagForTests(default);

            Assert.IsFalse(force.TryValidate(out string error));
            StringAssert.Contains("Event Tag", error);
        }

        [Test]
        public void ADebugDrawThatIsNotInstantIsRejected()
        {
            DebugDrawEffectDefinition draw =
                Own(ScriptableObject.CreateInstance<DebugDrawEffectDefinition>());
            draw.name = "GE_Debug_Test";
            draw.ConfigureDrawForTests(HitShape.Sphere(Vector3.zero, 1f), Color.red, 1f);
            draw.ConfigureForTests(
                GameplayEffectDurationPolicy.Duration,
                draw.Targeting,
                System.Array.Empty<GameplayEffectModifier>(),
                durationSeconds: 2f);

            Assert.IsFalse(draw.TryValidate(out string error));
            StringAssert.Contains("Instant policy", error);
        }

        // ---------------------------------------------------------- the chaining

        /// <summary>
        /// The reason effect validation exists at all: an effect is reached
        /// through the step that fires it, so an ability holding a broken one
        /// must report it rather than failing quietly at the damage frame.
        /// </summary>
        [Test]
        public void AStepReportsABrokenEffectByStepAndTriggerAndName()
        {
            DamageEffectDefinition damage = NewDamage();
            damage.name = "GE_Damage_Broken";
            damage.ConfigureDamageForTests(0f, Health);

            AbilityStep step = new();
            step.ConfigureForTests(1, 2, 30, 60f);
            step.SetEffectTriggersForTests(new[] { new AbilityEffectTrigger(4, damage) });

            Assert.IsFalse(step.TryValidate(2, out string error));
            StringAssert.Contains("Step 2", error);
            StringAssert.Contains("effect trigger 1", error);
            StringAssert.Contains("GE_Damage_Broken", error);
            StringAssert.Contains("Damage must be greater than zero", error);
        }

        [Test]
        public void AnAbilityIsInvalidWhileAStepEffectIs()
        {
            DamageEffectDefinition damage = NewDamage();
            damage.ConfigureDamageForTests(0f, Health);

            AbilityStep step = new();
            step.ConfigureForTests(1, 2, 30, 60f);
            step.SetEffectTriggersForTests(new[] { new AbilityEffectTrigger(4, damage) });

            TimelineAbilityDefinition ability = Own(ScriptableObject.CreateInstance<TimelineAbilityDefinition>());
            ability.SetAbilityIdForTests("test.validation.chain");
            ability.SetStepsForTests(step);

            Assert.IsFalse(ability.TryValidate(out string error));
            StringAssert.Contains("Damage must be greater than zero", error);
        }

        [Test]
        public void AnAbilityReportsABrokenParryEffect()
        {
            GameplayEffectDefinition heal = NewEffect(GameplayEffectDurationPolicy.Duration,
                new[] { Add(Health, 5f) }, duration: 0f);
            heal.name = "GE_Heal_Broken";

            TimelineAbilityDefinition ability = Own(ScriptableObject.CreateInstance<TimelineAbilityDefinition>());
            ability.SetAbilityIdForTests("test.validation.parry");
            ability.SetParryEffectsForTests(heal);

            Assert.IsFalse(ability.TryValidate(out string error));
            StringAssert.Contains("GE_Heal_Broken", error);
            StringAssert.Contains("Duration effect", error);
        }

        [Test]
        public void AnAbilityWithAValidEffectStillValidates()
        {
            DamageEffectDefinition damage = NewDamage();
            damage.ConfigureDamageForTests(12f, Health);

            AbilityStep step = new();
            step.ConfigureForTests(1, 2, 30, 60f);
            step.SetEffectTriggersForTests(new[] { new AbilityEffectTrigger(4, damage) });

            TimelineAbilityDefinition ability = Own(ScriptableObject.CreateInstance<TimelineAbilityDefinition>());
            ability.SetAbilityIdForTests("test.validation.ok");
            ability.SetStepsForTests(step);

            Assert.IsTrue(ability.TryValidate(out string error), error);
        }

        // ------------------------------------------------------------ helpers

        private DamageEffectDefinition NewDamage()
        {
            DamageEffectDefinition damage =
                Own(ScriptableObject.CreateInstance<DamageEffectDefinition>());
            damage.name = "GE_Damage_Test";
            damage.SetTargetingForTests(GameplayEffectTargeting.FromShape(
                HitShape.Sphere(Vector3.forward, 1f).WithLayers(EnemyLayerMask)));
            damage.ConfigureDamageForTests(10f, Health);
            return damage;
        }
    }
}
