using NUnit.Framework;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    /// <summary>
    /// The three duration policies. Instant changes base values and leaves
    /// nothing behind; Duration and Infinite contribute to the current value
    /// while they live and clean up completely when they end.
    /// </summary>
    public sealed class GameplayEffectLifecycleTests : GameplayEffectTestBase
    {
        [Test]
        public void InstantEffect_ChangesBaseValue_Deterministically()
        {
            GameObject source = NewBareActor("Source");
            GameObject target = NewActor("Target", Health, 100f);
            GameplayEffectDefinition effect = NewEffect(
                GameplayEffectDurationPolicy.Instant, new[] { Add(Health, -25f) });

            GameplayEffectApplicationResult result = Apply(effect, source, target);

            Assert.AreEqual(GameplayEffectApplicationOutcome.Executed, result.Outcome);
            Assert.AreEqual(75f, Attributes(target).GetBase(Health));
            Assert.AreEqual(75f, Attributes(target).GetCurrent(Health));
            Assert.IsFalse(result.Handle.IsValid, "An instant effect names no active effect.");
            Assert.AreEqual(0, Effects(target).ActiveEffectCount);
        }

        [Test]
        public void InstantEffect_FiresTheAttributeChange_WithTheSourceOnIt()
        {
            GameObject source = NewBareActor("Source");
            GameObject target = NewActor("Target", Health, 100f);
            GameplayEffectDefinition effect = NewEffect(
                GameplayEffectDurationPolicy.Instant, new[] { Add(Health, -25f) });

            AttributeValueChanged? observed = null;
            Attributes(target).Changed += change => observed = change;
            Apply(effect, source, target);

            Assert.IsTrue(observed.HasValue);
            Assert.AreEqual(100f, observed.Value.OldValue);
            Assert.AreEqual(75f, observed.Value.NewValue);
            Assert.AreEqual(source, observed.Value.Source);
        }

        [Test]
        public void InstantEffect_AppliedRepeatedly_AccumulatesOnTheBaseValue()
        {
            GameObject source = NewBareActor("Source");
            GameObject target = NewActor("Target", Health, 100f);
            GameplayEffectDefinition effect = NewEffect(
                GameplayEffectDurationPolicy.Instant, new[] { Add(Health, -10f) });

            for (int i = 0; i < 4; i++)
            {
                Apply(effect, source, target);
            }

            Assert.AreEqual(60f, Attributes(target).GetBase(Health));
        }

        [Test]
        public void DurationEffect_ContributesToCurrentValue_AndLeavesTheBaseAlone()
        {
            GameObject source = NewBareActor("Source");
            GameObject target = NewActor("Target", Health, 50f);
            GameplayEffectDefinition effect = NewEffect(
                GameplayEffectDurationPolicy.Duration, new[] { Add(Health, 20f) }, 2f);

            GameplayEffectApplicationResult result = Apply(effect, source, target);

            Assert.AreEqual(GameplayEffectApplicationOutcome.Applied, result.Outcome);
            Assert.IsTrue(result.Handle.IsValid);
            Assert.AreEqual(70f, Attributes(target).GetCurrent(Health));
            Assert.AreEqual(50f, Attributes(target).GetBase(Health), "A duration effect never folds into the base.");
        }

        [Test]
        public void DurationEffect_ExpiringCleansUpCompletely()
        {
            GameObject source = NewBareActor("Source");
            GameObject target = NewActor("Target", Health, 50f);
            GameplayEffectDefinition effect = NewEffect(
                GameplayEffectDurationPolicy.Duration, new[] { Add(Health, 20f) }, 2f);
            GameplayEffectContainer container = Effects(target);

            GameplayEffectHandle handle = Apply(effect, source, target).Handle;
            container.Tick(1f);
            Assert.AreEqual(70f, Attributes(target).GetCurrent(Health));

            container.Tick(1.5f);

            Assert.AreEqual(50f, Attributes(target).GetCurrent(Health));
            Assert.AreEqual(0, container.ActiveEffectCount);
            Assert.IsFalse(container.IsActive(handle));
        }

        [Test]
        public void InfiniteEffect_NeverExpiresButIsRemovable()
        {
            GameObject source = NewBareActor("Source");
            GameObject target = NewActor("Target", Health, 50f);
            GameplayEffectDefinition effect = NewEffect(
                GameplayEffectDurationPolicy.Infinite, new[] { Add(Health, 20f) });
            GameplayEffectContainer container = Effects(target);

            GameplayEffectHandle handle = Apply(effect, source, target).Handle;
            for (int i = 0; i < 100; i++)
            {
                container.Tick(1f);
            }

            Assert.AreEqual(70f, Attributes(target).GetCurrent(Health));
            Assert.IsTrue(container.IsActive(handle));
            Assert.IsTrue(float.IsPositiveInfinity(container.GetRemainingDuration(handle)));

            Assert.IsTrue(container.TryRemove(handle));
            Assert.AreEqual(50f, Attributes(target).GetCurrent(Health));
            Assert.AreEqual(0, container.ActiveEffectCount);
        }

        [Test]
        public void PeriodicEffect_FoldsIntoTheBaseValueOncePerPeriod()
        {
            GameObject source = NewBareActor("Source");
            GameObject target = NewActor("Target", Health, 100f);
            GameplayEffectDefinition effect = NewEffect(
                GameplayEffectDurationPolicy.Duration, new[] { Add(Health, -5f) }, 3f, 1f);
            effect.SetExecutePeriodOnApplicationForTests(false);
            GameplayEffectContainer container = Effects(target);

            Apply(effect, source, target);
            Assert.AreEqual(100f, Attributes(target).GetBase(Health), "No tick has elapsed yet.");

            container.Tick(1f);
            Assert.AreEqual(95f, Attributes(target).GetBase(Health));
            container.Tick(1f);
            container.Tick(1f);

            Assert.AreEqual(85f, Attributes(target).GetBase(Health));
            Assert.AreEqual(0, container.ActiveEffectCount, "The effect ends with its last period.");
        }

        [Test]
        public void PeriodicEffect_CanRunItsFirstPeriodOnApplication()
        {
            GameObject source = NewBareActor("Source");
            GameObject target = NewActor("Target", Health, 100f);
            GameplayEffectDefinition effect = NewEffect(
                GameplayEffectDurationPolicy.Duration, new[] { Add(Health, -5f) }, 3f, 1f);

            Apply(effect, source, target);

            Assert.AreEqual(95f, Attributes(target).GetBase(Health));
        }

        [Test]
        public void PeriodicEffect_RemovedEarly_StopsTickingAndRefundsNothing()
        {
            GameObject source = NewBareActor("Source");
            GameObject target = NewActor("Target", Health, 100f);
            GameplayEffectDefinition effect = NewEffect(
                GameplayEffectDurationPolicy.Duration, new[] { Add(Health, -5f) }, 10f, 1f);
            effect.SetExecutePeriodOnApplicationForTests(false);
            GameplayEffectContainer container = Effects(target);

            GameplayEffectHandle handle = Apply(effect, source, target).Handle;
            container.Tick(2f);
            Assert.AreEqual(90f, Attributes(target).GetBase(Health));

            container.TryRemove(handle);
            container.Tick(5f);

            Assert.AreEqual(90f, Attributes(target).GetBase(Health));
        }

        [Test]
        public void DurationMagnitude_CanBeCapturedFromASourceAttribute()
        {
            GameObject source = NewActor("Source", Strength, 4f);
            GameObject target = NewActor("Target", Health, 50f);
            GameplayEffectDefinition effect = NewEffect(
                GameplayEffectDurationPolicy.Duration, new[] { Add(Health, 5f) }, 1f);
            effect.SetDurationMagnitudeForTests(
                new GameplayEffectMagnitude(0f, Strength, 1f, GameplayEffectCaptureSource.Source));
            GameplayEffectContainer container = Effects(target);

            GameplayEffectHandle handle = Apply(effect, source, target).Handle;

            Assert.AreEqual(4f, container.GetRemainingDuration(handle), 0.001f);
        }

        [Test]
        public void EffectWithNoAttributeSet_StillGrantsItsTags()
        {
            GameplayTag marked = new("Test.Marked");
            GameObject source = NewBareActor("Source");
            GameObject target = NewBareActor("Target");
            GameplayEffectDefinition effect = NewEffect(GameplayEffectDurationPolicy.Infinite);
            effect.SetTagsForTests(granted: new[] { marked });

            GameplayEffectContainer container = Effects(target);
            GameplayEffectHandle handle = Apply(effect, source, target).Handle;

            Assert.IsTrue(container.HasEffectTag(marked));
            container.TryRemove(handle);
            Assert.IsFalse(container.HasEffectTag(marked));
        }
    }
}
