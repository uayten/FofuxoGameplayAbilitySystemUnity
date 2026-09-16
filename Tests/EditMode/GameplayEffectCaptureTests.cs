using NUnit.Framework;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    /// <summary>
    /// The capture rules: whose attribute a magnitude reads, and whether it was
    /// read once at application or is re-read while the effect is active. They
    /// are declared on the magnitude and resolved by the spec, never decided at
    /// the point of application.
    /// </summary>
    public sealed class GameplayEffectCaptureTests : GameplayEffectTestBase
    {
        private static GameplayEffectModifier Scaled(
            GameplayAttribute attribute,
            GameplayAttribute captured,
            GameplayEffectCaptureSource capture,
            bool snapshot,
            float coefficient = 1f)
        {
            return new GameplayEffectModifier(
                attribute,
                AttributeOperation.Add,
                new GameplayEffectMagnitude(0f, captured, coefficient, capture, snapshot));
        }

        [Test]
        public void SnapshotMagnitude_KeepsTheValueTheSourceHadAtApplication()
        {
            GameObject source = NewActor("Source", Strength, 4f);
            GameObject target = NewActor("Target", Health, 50f);
            GameplayEffectDefinition effect = NewEffect(
                GameplayEffectDurationPolicy.Infinite,
                new[] { Scaled(Health, Strength, GameplayEffectCaptureSource.Source, true) });
            GameplayEffectContainer container = Effects(target);

            Apply(effect, source, target);
            Assert.AreEqual(54f, Attributes(target).GetCurrent(Health));

            Attributes(source).ApplyInstantModifier(
                new AttributeModifier(Strength, AttributeOperation.Add, 6f));
            container.Tick(1f);

            Assert.AreEqual(54f, Attributes(target).GetCurrent(Health),
                "A snapshot never re-reads the attribute it captured.");
        }

        [Test]
        public void LiveMagnitude_FollowsTheSourceWhileTheEffectIsActive()
        {
            GameObject source = NewActor("Source", Strength, 4f);
            GameObject target = NewActor("Target", Health, 50f);
            GameplayEffectDefinition effect = NewEffect(
                GameplayEffectDurationPolicy.Infinite,
                new[] { Scaled(Health, Strength, GameplayEffectCaptureSource.Source, false) });
            GameplayEffectContainer container = Effects(target);

            Apply(effect, source, target);
            Assert.AreEqual(54f, Attributes(target).GetCurrent(Health));

            Attributes(source).ApplyInstantModifier(
                new AttributeModifier(Strength, AttributeOperation.Add, 6f));
            container.Tick(1f);

            Assert.AreEqual(60f, Attributes(target).GetCurrent(Health));
        }

        [Test]
        public void TargetCapture_ReadsTheActorTheEffectLandedOn()
        {
            GameObject source = NewActor("Source", Strength, 4f);
            GameObject target = NewActor(
                "Target",
                new AttributeSet.InitialValue(Health, 50f, 0f, 100f),
                new AttributeSet.InitialValue(Armor, 7f, 0f, 100f));
            GameplayEffectDefinition effect = NewEffect(
                GameplayEffectDurationPolicy.Infinite,
                new[] { Scaled(Health, Armor, GameplayEffectCaptureSource.Target, true) });

            Apply(effect, source, target);

            Assert.AreEqual(57f, Attributes(target).GetCurrent(Health));
        }

        [Test]
        public void InstantEffect_ReadsTheSameValue_SnapshotOrNot()
        {
            GameObject source = NewActor("Source", Strength, 4f);
            GameObject snapshotTarget = NewActor("SnapshotTarget", Health, 50f);
            GameObject liveTarget = NewActor("LiveTarget", Health, 50f);
            GameplayEffectDefinition snapshotEffect = NewEffect(
                GameplayEffectDurationPolicy.Instant,
                new[] { Scaled(Health, Strength, GameplayEffectCaptureSource.Source, true) });
            GameplayEffectDefinition liveEffect = NewEffect(
                GameplayEffectDurationPolicy.Instant,
                new[] { Scaled(Health, Strength, GameplayEffectCaptureSource.Source, false) });

            Apply(snapshotEffect, source, snapshotTarget);
            Apply(liveEffect, source, liveTarget);

            Assert.AreEqual(54f, Attributes(snapshotTarget).GetBase(Health));
            Assert.AreEqual(54f, Attributes(liveTarget).GetBase(Health));
        }

        [Test]
        public void CapturedValue_IsReadableOffTheSpec()
        {
            GameObject source = NewActor("Source", Strength, 9f);
            GameObject target = NewActor("Target", Health, 50f);
            GameplayEffectDefinition effect = NewEffect(
                GameplayEffectDurationPolicy.Infinite,
                new[] { Scaled(Health, Strength, GameplayEffectCaptureSource.Source, true, 2f) });

            GameplayEffectHandle handle = Apply(effect, source, target).Handle;
            Effects(target).TryGetActiveEffect(handle, out ActiveGameplayEffect active);

            Assert.IsTrue(active.Spec.TryGetSnapshot(
                GameplayEffectCaptureSource.Source, Strength, out float captured));
            Assert.AreEqual(9f, captured);
            Assert.AreEqual(18f, active.Spec.ModifierMagnitudes[0]);
        }

        [Test]
        public void MissingAttributeSetOnTheSource_CapturesZero()
        {
            GameObject source = NewBareActor("Source");
            GameObject target = NewActor("Target", Health, 50f);
            GameplayEffectDefinition effect = NewEffect(
                GameplayEffectDurationPolicy.Instant,
                new[] { Scaled(Health, Strength, GameplayEffectCaptureSource.Source, true) });

            Assert.DoesNotThrow(() => Apply(effect, source, target));
            Assert.AreEqual(50f, Attributes(target).GetBase(Health));
        }

        [Test]
        public void LevelCurve_ScalesTheMagnitude()
        {
            GameObject source = NewBareActor("Source");
            GameObject target = NewActor("Target", Health, 0f, 1000f);
            GameplayEffectDefinition effect = NewEffect(GameplayEffectDurationPolicy.Instant);
            effect.SetModifiersForTests(new GameplayEffectModifier(
                Health,
                AttributeOperation.Add,
                new GameplayEffectMagnitude(10f).WithLevelCurve(
                    new AnimationCurve(new Keyframe(1f, 1f), new Keyframe(100f, 10f)))));

            Apply(effect, source, target, 100);

            Assert.AreEqual(100f, Attributes(target).GetBase(Health), 0.01f);
        }
    }
}
