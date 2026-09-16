using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    /// <summary>
    /// The spec is the per-application data and the definition is shared,
    /// immutable authoring data. This fixture is the guard on that line.
    /// </summary>
    public sealed class GameplayEffectSpecTests : GameplayEffectTestBase
    {
        [Test]
        public void RepeatedApplications_LeaveTheAssetByteIdentical()
        {
            GameObject source = NewActor("Source", Strength, 6f);
            GameObject first = NewActor("First", Health, 100f);
            GameObject second = NewActor("Second", Health, 100f);
            GameplayEffectDefinition effect = NewEffect(
                GameplayEffectDurationPolicy.Duration, null, 3f, 1f);
            effect.SetModifiersForTests(new GameplayEffectModifier(
                Health,
                AttributeOperation.Add,
                new GameplayEffectMagnitude(
                    -5f, Strength, 2f, GameplayEffectCaptureSource.Source, snapshot: false)));
            effect.SetStackingForTests(GameplayEffectStacking.With(EffectStacking.Stack, 3));
            effect.SetTagsForTests(
                effect: new[] { new GameplayTag("Test.Effect.Poison") },
                granted: new[] { new GameplayTag("Test.Poisoned") });

            string before = JsonUtility.ToJson(effect);

            for (int i = 0; i < 20; i++)
            {
                Apply(effect, source, first, level: 1 + i % 7);
                Apply(effect, source, second, level: 100 - i);
                Effects(first).Tick(0.4f);
                Effects(second).Tick(0.4f);
            }

            Assert.AreEqual(before, JsonUtility.ToJson(effect),
                "An application must never write back into its definition.");
        }

        [Test]
        public void EachTarget_GetsItsOwnSpec()
        {
            GameObject source = NewBareActor("Source");
            GameObject first = NewActor("First", Health, 100f);
            GameObject second = NewActor("Second", Health, 40f);
            GameplayEffectDefinition effect = NewEffect(
                GameplayEffectDurationPolicy.Infinite, new[] { Add(Health, 10f) });

            GameplayEffectHandle firstHandle = Apply(effect, source, first).Handle;
            GameplayEffectHandle secondHandle = Apply(effect, source, second).Handle;

            Effects(first).TryGetActiveEffect(firstHandle, out ActiveGameplayEffect firstActive);
            Effects(second).TryGetActiveEffect(secondHandle, out ActiveGameplayEffect secondActive);

            Assert.AreNotSame(firstActive.Spec, secondActive.Spec);
            Assert.AreSame(effect, firstActive.Spec.Definition);
            Assert.AreSame(effect, secondActive.Spec.Definition);
            Assert.AreEqual(first, firstActive.Spec.Target);
            Assert.AreEqual(second, secondActive.Spec.Target);
        }

        [Test]
        public void SpecCarriesSourceTargetAndLevel()
        {
            GameObject source = NewBareActor("Source");
            GameObject target = NewActor("Target", Health, 100f);
            GameplayEffectDefinition effect = NewEffect(
                GameplayEffectDurationPolicy.Infinite, new[] { Add(Health, 1f) });

            GameplayEffectHandle handle = Apply(effect, source, target, level: 42).Handle;
            Effects(target).TryGetActiveEffect(handle, out ActiveGameplayEffect active);

            Assert.AreEqual(source, active.Spec.Source);
            Assert.AreEqual(target, active.Spec.Target);
            Assert.AreEqual(42, active.Spec.Level);
            Assert.AreEqual(-1, active.Spec.TriggerIndex, "No ability trigger fired this one.");
        }

        [Test]
        public void SpecLevel_ClampsToTheAuthoredRange()
        {
            GameObject source = NewBareActor("Source");
            GameObject target = NewActor("Target", Health, 100f);
            GameplayEffectDefinition effect = NewEffect(GameplayEffectDurationPolicy.Instant);

            Assert.AreEqual(1, new GameplayEffectSpec(effect, source, target, 0).Level);
            Assert.AreEqual(1, new GameplayEffectSpec(effect, source, target, -5).Level);
            Assert.AreEqual(100, new GameplayEffectSpec(effect, source, target, 900).Level);
        }

        [Test]
        public void DynamicModifier_AppliesWithoutAnAsset()
        {
            GameObject source = NewBareActor("Source");
            GameObject target = NewActor("Target", Health, 50f);
            GameplayEffectContainer container = Effects(target);

            GameplayEffectApplicationResult result = container.ApplyDynamic(
                GameplayEffectDurationPolicy.Duration,
                Health,
                AttributeOperation.Add,
                20f,
                durationSeconds: 2f,
                source: source);

            Assert.AreEqual(GameplayEffectApplicationOutcome.Applied, result.Outcome);
            Assert.AreEqual(70f, Attributes(target).GetCurrent(Health));

            container.Tick(2f);

            Assert.AreEqual(50f, Attributes(target).GetCurrent(Health));
            Assert.IsFalse(container.IsActive(result.Handle));
        }

        [Test]
        public void DynamicApplications_ShareOneStatelessDefinition()
        {
            GameObject source = NewBareActor("Source");
            GameObject target = NewActor("Target", Health, 50f, 500f);
            GameplayEffectContainer container = Effects(target);

            container.ApplyDynamic(
                GameplayEffectDurationPolicy.Duration, Health, AttributeOperation.Add, 20f,
                durationSeconds: 4f, source: source);
            container.ApplyDynamic(
                GameplayEffectDurationPolicy.Duration, Health, AttributeOperation.Add, 5f,
                durationSeconds: 1f, source: source);

            // Refresh stacking against one shared definition and one source, so
            // the second application replaces the first. What matters is that the
            // magnitude and the duration came from the specs, not the asset.
            Assert.AreEqual(1, container.ActiveEffectCount);
            Assert.AreEqual(55f, Attributes(target).GetCurrent(Health));
        }

        [Test]
        public void AppliedThroughTheSystem_RunsWithoutAnActivation()
        {
            GameObject owner = NewActor("Owner", Health, 40f);
            AbilitySystem system = owner.AddComponent<AbilitySystem>();
            GameplayEffectDefinition effect = NewEffect(
                GameplayEffectDurationPolicy.Instant,
                new[] { Add(Health, 10f) },
                mode: GameplayEffectTargetMode.Owner);

            int applied = system.ApplyGameplayEffect(
                effect, AbilityContext.FromTarget(owner, owner));

            Assert.AreEqual(1, applied);
            Assert.AreEqual(50f, Attributes(owner).GetBase(Health));
            Assert.AreEqual(0, system.ActiveAbilityCount,
                "No ephemeral activation is invented to carry the effect.");
        }

        [Test]
        public void SpecMagnitudes_AreOnePerModifier()
        {
            GameObject source = NewBareActor("Source");
            GameObject target = NewActor(
                "Target",
                new AttributeSet.InitialValue(Health, 50f, 0f, 500f),
                new AttributeSet.InitialValue(Armor, 5f, 0f, 500f));
            GameplayEffectDefinition effect = NewEffect(
                GameplayEffectDurationPolicy.Infinite,
                new[] { Add(Health, 12f), Add(Armor, 3f) });

            GameplayEffectHandle handle = Apply(effect, source, target).Handle;
            Effects(target).TryGetActiveEffect(handle, out ActiveGameplayEffect active);
            IReadOnlyList<float> magnitudes = active.Spec.ModifierMagnitudes;

            Assert.AreEqual(2, magnitudes.Count);
            Assert.AreEqual(12f, magnitudes[0]);
            Assert.AreEqual(3f, magnitudes[1]);
            Assert.AreEqual(62f, Attributes(target).GetCurrent(Health));
            Assert.AreEqual(8f, Attributes(target).GetCurrent(Armor));
        }
    }
}
