using NUnit.Framework;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    /// <summary>
    /// Stack, refresh, ignore and overflow, each on its own. They are separate
    /// decisions, and a change to one of them must not be able to hide in
    /// another one passing.
    /// </summary>
    public sealed class GameplayEffectStackingTests : GameplayEffectTestBase
    {
        [Test]
        public void Stack_AddsOneContributionPerStack()
        {
            GameObject source = NewBareActor("Source");
            GameObject target = NewActor("Target", Health, 10f, 500f);
            GameplayEffectDefinition effect = NewEffect(
                GameplayEffectDurationPolicy.Infinite, new[] { Add(Health, 20f) });
            effect.SetStackingForTests(GameplayEffectStacking.With(EffectStacking.Stack));
            GameplayEffectContainer container = Effects(target);

            GameplayEffectHandle first = Apply(effect, source, target).Handle;
            GameplayEffectApplicationResult second = Apply(effect, source, target);

            Assert.AreEqual(GameplayEffectApplicationOutcome.Stacked, second.Outcome);
            Assert.AreEqual(first, second.Handle, "A stack reuses the handle it stacked onto.");
            Assert.AreEqual(2, container.GetStackCount(first));
            Assert.AreEqual(1, container.ActiveEffectCount);
            Assert.AreEqual(50f, Attributes(target).GetCurrent(Health));
        }

        [Test]
        public void Stack_MultiplyCompoundsPerStack()
        {
            GameObject source = NewBareActor("Source");
            GameObject target = NewActor("Target", Health, 100f, 1000f);
            GameplayEffectDefinition effect = NewEffect(
                GameplayEffectDurationPolicy.Infinite,
                new[]
                {
                    new GameplayEffectModifier(Health, AttributeOperation.Multiply, 1f),
                });
            effect.SetStackingForTests(GameplayEffectStacking.With(EffectStacking.Stack));

            Apply(effect, source, target);
            Apply(effect, source, target);

            Assert.AreEqual(400f, Attributes(target).GetCurrent(Health), 0.001f);
        }

        [Test]
        public void RemovingOneStack_LeavesTheRest()
        {
            GameObject source = NewBareActor("Source");
            GameObject target = NewActor("Target", Health, 10f, 500f);
            GameplayEffectDefinition effect = NewEffect(
                GameplayEffectDurationPolicy.Infinite, new[] { Add(Health, 20f) });
            effect.SetStackingForTests(GameplayEffectStacking.With(EffectStacking.Stack));
            GameplayEffectContainer container = Effects(target);

            GameplayEffectHandle handle = Apply(effect, source, target).Handle;
            Apply(effect, source, target);
            Apply(effect, source, target);
            Assert.AreEqual(70f, Attributes(target).GetCurrent(Health));

            container.TryRemoveStack(handle);

            Assert.AreEqual(2, container.GetStackCount(handle));
            Assert.AreEqual(50f, Attributes(target).GetCurrent(Health));
        }

        [Test]
        public void Refresh_KeepsOneContributionAndRestartsTheDuration()
        {
            GameObject source = NewBareActor("Source");
            GameObject target = NewActor("Target", Health, 10f, 500f);
            GameplayEffectDefinition effect = NewEffect(
                GameplayEffectDurationPolicy.Duration, new[] { Add(Health, 20f) }, 4f);
            effect.SetStackingForTests(GameplayEffectStacking.With(EffectStacking.Refresh));
            GameplayEffectContainer container = Effects(target);

            GameplayEffectHandle handle = Apply(effect, source, target).Handle;
            container.Tick(3f);
            Assert.AreEqual(1f, container.GetRemainingDuration(handle), 0.001f);

            Apply(effect, source, target);

            Assert.AreEqual(1, container.GetStackCount(handle));
            Assert.AreEqual(4f, container.GetRemainingDuration(handle), 0.001f);
            Assert.AreEqual(30f, Attributes(target).GetCurrent(Health));
        }

        [Test]
        public void Ignore_LeavesTheFirstApplicationUntouched()
        {
            GameObject source = NewBareActor("Source");
            GameObject target = NewActor("Target", Health, 10f, 500f);
            GameplayEffectDefinition effect = NewEffect(
                GameplayEffectDurationPolicy.Duration, new[] { Add(Health, 20f) }, 4f);
            effect.SetStackingForTests(GameplayEffectStacking.With(EffectStacking.Ignore));
            GameplayEffectContainer container = Effects(target);

            GameplayEffectHandle handle = Apply(effect, source, target).Handle;
            container.Tick(3f);

            Apply(effect, source, target);

            Assert.AreEqual(1, container.GetStackCount(handle));
            Assert.AreEqual(1f, container.GetRemainingDuration(handle), 0.001f);
            Assert.AreEqual(30f, Attributes(target).GetCurrent(Health));
        }

        [Test]
        public void StackScope_BySource_KeepsOneStackPerAttacker()
        {
            GameObject first = NewBareActor("SourceA");
            GameObject second = NewBareActor("SourceB");
            GameObject target = NewActor("Target", Health, 10f, 500f);
            GameplayEffectDefinition effect = NewEffect(
                GameplayEffectDurationPolicy.Infinite, new[] { Add(Health, 20f) });
            effect.SetStackingForTests(GameplayEffectStacking.With(EffectStacking.Stack));

            Apply(effect, first, target);
            Apply(effect, second, target);

            Assert.AreEqual(2, Effects(target).ActiveEffectCount);
            Assert.AreEqual(50f, Attributes(target).GetCurrent(Health));
        }

        [Test]
        public void StackScope_ByTarget_CollapsesEveryAttackerIntoOneStack()
        {
            GameObject first = NewBareActor("SourceA");
            GameObject second = NewBareActor("SourceB");
            GameObject target = NewActor("Target", Health, 10f, 500f);
            GameplayEffectDefinition effect = NewEffect(
                GameplayEffectDurationPolicy.Infinite, new[] { Add(Health, 20f) });
            effect.SetStackingForTests(GameplayEffectStacking.With(
                EffectStacking.Stack, 0, GameplayEffectStackScope.ByTarget));

            GameplayEffectHandle handle = Apply(effect, first, target).Handle;
            Apply(effect, second, target);

            Assert.AreEqual(1, Effects(target).ActiveEffectCount);
            Assert.AreEqual(2, Effects(target).GetStackCount(handle));
        }

        [Test]
        public void Overflow_StopsAtTheLimit()
        {
            GameObject source = NewBareActor("Source");
            GameObject target = NewActor("Target", Health, 10f, 500f);
            GameplayEffectDefinition effect = NewEffect(
                GameplayEffectDurationPolicy.Infinite, new[] { Add(Health, 20f) });
            effect.SetStackingForTests(GameplayEffectStacking.With(EffectStacking.Stack, 2));
            GameplayEffectContainer container = Effects(target);

            GameplayEffectHandle handle = Apply(effect, source, target).Handle;
            Apply(effect, source, target);
            GameplayEffectApplicationResult overflowed = Apply(effect, source, target);

            Assert.AreEqual(GameplayEffectApplicationOutcome.Overflowed, overflowed.Outcome);
            Assert.AreEqual(2, container.GetStackCount(handle));
            Assert.AreEqual(50f, Attributes(target).GetCurrent(Health));
        }

        [Test]
        public void Overflow_RunsItsOverflowEffects()
        {
            GameObject source = NewBareActor("Source");
            GameObject target = NewActor("Target", Health, 100f, 500f);
            GameplayEffectDefinition punish = NewEffect(
                GameplayEffectDurationPolicy.Instant, new[] { Add(Health, -30f) });
            GameplayEffectDefinition effect = NewEffect(
                GameplayEffectDurationPolicy.Infinite, new[] { Add(Health, 0f) });
            effect.SetStackingForTests(
                GameplayEffectStacking.With(EffectStacking.Stack, 1)
                    .WithOverflow(new[] { punish }));

            Apply(effect, source, target);
            Assert.AreEqual(100f, Attributes(target).GetBase(Health));

            Apply(effect, source, target);

            Assert.AreEqual(70f, Attributes(target).GetBase(Health));
        }

        [Test]
        public void Overflow_CanClearTheWholeStack()
        {
            GameObject source = NewBareActor("Source");
            GameObject target = NewActor("Target", Health, 10f, 500f);
            GameplayEffectDefinition effect = NewEffect(
                GameplayEffectDurationPolicy.Infinite, new[] { Add(Health, 20f) });
            effect.SetStackingForTests(
                GameplayEffectStacking.With(EffectStacking.Stack, 2)
                    .WithOverflow(null, denyApplication: true, clearStack: true));
            GameplayEffectContainer container = Effects(target);

            GameplayEffectHandle handle = Apply(effect, source, target).Handle;
            Apply(effect, source, target);
            Apply(effect, source, target);

            Assert.AreEqual(0, container.ActiveEffectCount);
            Assert.IsFalse(container.IsActive(handle));
            Assert.AreEqual(10f, Attributes(target).GetCurrent(Health));
        }

        [Test]
        public void Overflow_DeniedApplication_DoesNotRefreshTheDuration()
        {
            GameObject source = NewBareActor("Source");
            GameObject target = NewActor("Target", Health, 10f, 500f);
            GameplayEffectDefinition effect = NewEffect(
                GameplayEffectDurationPolicy.Duration, new[] { Add(Health, 20f) }, 4f);
            effect.SetStackingForTests(
                GameplayEffectStacking.With(EffectStacking.Stack, 1)
                    .WithOverflow(null, denyApplication: true));
            GameplayEffectContainer container = Effects(target);

            GameplayEffectHandle handle = Apply(effect, source, target).Handle;
            container.Tick(3f);
            Apply(effect, source, target);

            Assert.AreEqual(1f, container.GetRemainingDuration(handle), 0.001f);
        }
    }
}
