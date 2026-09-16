using NUnit.Framework;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    /// <summary>
    /// Handles: what removal by (attribute, source) could not express, and what
    /// a handle answers once the effect it names is gone.
    /// </summary>
    public sealed class GameplayEffectHandleTests : GameplayEffectTestBase
    {
        [Test]
        public void TwoApplicationsOfOneSource_AreTwoHandles()
        {
            GameObject source = NewBareActor("Source");
            GameObject target = NewActor("Target", Health, 10f, 500f);
            // Two effects, one attribute, one source: exactly the case removal by
            // (attribute, source) could not tell apart.
            GameplayEffectDefinition first = NewEffect(
                GameplayEffectDurationPolicy.Infinite, new[] { Add(Health, 20f) });
            GameplayEffectDefinition second = NewEffect(
                GameplayEffectDurationPolicy.Infinite, new[] { Add(Health, 5f) });
            GameplayEffectContainer container = Effects(target);

            GameplayEffectHandle firstHandle = Apply(first, source, target).Handle;
            GameplayEffectHandle secondHandle = Apply(second, source, target).Handle;

            Assert.AreNotEqual(firstHandle, secondHandle);
            Assert.AreEqual(35f, Attributes(target).GetCurrent(Health));

            Assert.IsTrue(container.TryRemove(firstHandle));

            Assert.AreEqual(15f, Attributes(target).GetCurrent(Health));
            Assert.IsFalse(container.IsActive(firstHandle));
            Assert.IsTrue(container.IsActive(secondHandle));
        }

        [Test]
        public void RemovedHandle_KeepsAnsweringInsteadOfNamingSomethingElse()
        {
            GameObject source = NewBareActor("Source");
            GameObject target = NewActor("Target", Health, 10f, 500f);
            GameplayEffectDefinition effect = NewEffect(
                GameplayEffectDurationPolicy.Duration, new[] { Add(Health, 20f) }, 5f);
            GameplayEffectContainer container = Effects(target);

            GameplayEffectHandle handle = Apply(effect, source, target).Handle;
            Assert.IsTrue(container.TryRemove(handle));

            Assert.IsTrue(handle.IsValid, "The handle stays a name; it just names nothing live.");
            Assert.IsFalse(container.IsActive(handle));
            Assert.AreEqual(0, container.GetStackCount(handle));
            Assert.AreEqual(0f, container.GetRemainingDuration(handle));
            Assert.IsFalse(container.TryRemove(handle));
            Assert.IsFalse(container.TryRefresh(handle));
            Assert.IsFalse(container.TryRemoveStack(handle));
            Assert.IsFalse(container.TryGetActiveEffect(handle, out _));

            // A later application must not be reachable through the dead handle.
            GameplayEffectHandle reapplied = Apply(effect, source, target).Handle;
            Assert.AreNotEqual(handle, reapplied);
            Assert.IsFalse(container.IsActive(handle));
            Assert.IsTrue(container.IsActive(reapplied));
        }

        [Test]
        public void ExpiredHandle_AnswersTheSameWayARemovedOneDoes()
        {
            GameObject source = NewBareActor("Source");
            GameObject target = NewActor("Target", Health, 10f, 500f);
            GameplayEffectDefinition effect = NewEffect(
                GameplayEffectDurationPolicy.Duration, new[] { Add(Health, 20f) }, 1f);
            GameplayEffectContainer container = Effects(target);

            GameplayEffectHandle handle = Apply(effect, source, target).Handle;
            container.Tick(1f);

            Assert.IsFalse(container.IsActive(handle));
            Assert.AreEqual(0, container.GetStackCount(handle));
            Assert.AreEqual(0f, container.GetRemainingDuration(handle));
        }

        [Test]
        public void NoneHandle_IsNeverActiveAnywhere()
        {
            GameObject target = NewActor("Target", Health, 10f);
            GameplayEffectContainer container = Effects(target);

            Assert.IsFalse(GameplayEffectHandle.None.IsValid);
            Assert.IsFalse(container.IsActive(GameplayEffectHandle.None));
            Assert.IsFalse(container.TryRemove(GameplayEffectHandle.None));
        }

        [Test]
        public void HandleFromAnotherActor_IsNotActiveHere()
        {
            GameObject source = NewBareActor("Source");
            GameObject first = NewActor("First", Health, 10f, 500f);
            GameObject second = NewActor("Second", Health, 10f, 500f);
            GameplayEffectDefinition effect = NewEffect(
                GameplayEffectDurationPolicy.Infinite, new[] { Add(Health, 20f) });

            GameplayEffectHandle handle = Apply(effect, source, first).Handle;

            Assert.IsTrue(Effects(first).IsActive(handle));
            Assert.IsFalse(Effects(second).IsActive(handle));
            Assert.IsFalse(Effects(second).TryRemove(handle));
        }

        [Test]
        public void Refresh_RestartsTheDurationAndRereadsTheSource()
        {
            GameObject source = NewActor("Source", Strength, 3f);
            GameObject target = NewActor("Target", Health, 10f, 500f);
            GameplayEffectDefinition effect = NewEffect(GameplayEffectDurationPolicy.Duration,
                null, 4f);
            effect.SetModifiersForTests(new GameplayEffectModifier(
                Health,
                AttributeOperation.Add,
                new GameplayEffectMagnitude(
                    0f, Strength, 1f, GameplayEffectCaptureSource.Source, snapshot: false)));
            GameplayEffectContainer container = Effects(target);

            GameplayEffectHandle handle = Apply(effect, source, target).Handle;
            container.Tick(3f);
            Attributes(source).ApplyInstantModifier(
                new AttributeModifier(Strength, AttributeOperation.Add, 5f));

            Assert.IsTrue(container.TryRefresh(handle));

            Assert.AreEqual(4f, container.GetRemainingDuration(handle), 0.001f);
            Assert.AreEqual(18f, Attributes(target).GetCurrent(Health));
        }
    }
}
