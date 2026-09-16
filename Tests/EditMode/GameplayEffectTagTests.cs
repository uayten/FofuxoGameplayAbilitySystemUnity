using NUnit.Framework;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    /// <summary>
    /// Everything an effect says with tags: what it grants while active, what
    /// the target must or must not hold for it to land, what it makes the target
    /// immune to, and what it removes on the way in.
    /// </summary>
    public sealed class GameplayEffectTagTests : GameplayEffectTestBase
    {
        private static readonly GameplayTag Burning = new("Test.Burning");
        private static readonly GameplayTag Wet = new("Test.Wet");
        private static readonly GameplayTag FireEffect = new("Test.Effect.Fire");
        private static readonly GameplayTag Marked = new("Test.Marked");

        [Test]
        public void GrantedTags_LastExactlyAsLongAsTheEffect()
        {
            GameObject source = NewBareActor("Source");
            GameObject target = NewActor("Target", Health, 50f);
            GameplayEffectDefinition effect = NewEffect(
                GameplayEffectDurationPolicy.Duration, null, 2f);
            effect.SetTagsForTests(granted: new[] { Burning });
            GameplayEffectContainer container = Effects(target);

            Apply(effect, source, target);
            Assert.IsTrue(container.HasEffectTag(Burning));

            container.Tick(2f);

            Assert.IsFalse(container.HasEffectTag(Burning));
        }

        [Test]
        public void GrantedTags_AreVisibleThroughTheAbilitySystem()
        {
            GameObject source = NewBareActor("Source");
            GameObject target = NewActor("Target", Health, 50f);
            AbilitySystem system = target.AddComponent<AbilitySystem>();
            GameplayEffectDefinition effect = NewEffect(GameplayEffectDurationPolicy.Infinite);
            effect.SetTagsForTests(granted: new[] { Burning });
            GameplayEffectContainer container = Effects(target);

            GameplayEffectHandle handle = Apply(effect, source, target).Handle;
            Assert.IsTrue(system.HasTag(Burning));

            container.TryRemove(handle);

            Assert.IsFalse(system.HasTag(Burning));
        }

        [Test]
        public void GrantedTags_AreRefcountedAcrossTwoEffects()
        {
            GameObject source = NewBareActor("Source");
            GameObject target = NewActor("Target", Health, 50f);
            GameplayEffectDefinition first = NewEffect(GameplayEffectDurationPolicy.Infinite);
            first.SetTagsForTests(granted: new[] { Burning });
            GameplayEffectDefinition second = NewEffect(GameplayEffectDurationPolicy.Infinite);
            second.SetTagsForTests(granted: new[] { Burning });
            GameplayEffectContainer container = Effects(target);

            GameplayEffectHandle firstHandle = Apply(first, source, target).Handle;
            Apply(second, source, target);

            container.TryRemove(firstHandle);

            Assert.IsTrue(container.HasEffectTag(Burning),
                "The second effect still grants it.");
        }

        [Test]
        public void ApplicationRequiredTags_BlockAnEffectTheTargetIsNotReadyFor()
        {
            GameObject source = NewBareActor("Source");
            GameObject target = NewActor("Target", Health, 100f);
            GameplayEffectDefinition effect = NewEffect(
                GameplayEffectDurationPolicy.Instant, new[] { Add(Health, -10f) });
            effect.SetTagsForTests(required: new[] { Wet });

            GameplayEffectApplicationResult blocked = Apply(effect, source, target);

            Assert.AreEqual(GameplayEffectApplicationOutcome.Blocked, blocked.Outcome);
            Assert.AreEqual(100f, Attributes(target).GetBase(Health));
        }

        [Test]
        public void ApplicationBlockedTags_RefuseAnInvulnerableTarget()
        {
            GameObject source = NewBareActor("Source");
            GameObject target = NewActor("Target", Health, 100f);
            AbilitySystem system = target.AddComponent<AbilitySystem>();
            system.SetLooseTag(CommonGameplayTags.Invulnerable, true);
            GameplayEffectDefinition effect = NewEffect(
                GameplayEffectDurationPolicy.Instant, new[] { Add(Health, -10f) });
            effect.SetTagsForTests(blocked: new[] { CommonGameplayTags.Invulnerable });

            GameplayEffectSpec refused = null;
            Effects(target).EffectBlocked += spec => refused = spec;
            GameplayEffectApplicationResult result = Apply(effect, source, target);

            Assert.AreEqual(GameplayEffectApplicationOutcome.Blocked, result.Outcome);
            Assert.AreEqual(100f, Attributes(target).GetBase(Health));
            Assert.IsNotNull(refused, "A blocked application is observable.");
            Assert.AreSame(effect, refused.Definition);

            system.SetLooseTag(CommonGameplayTags.Invulnerable, false);
            Apply(effect, source, target);
            Assert.AreEqual(90f, Attributes(target).GetBase(Health));
        }

        [Test]
        public void GrantedImmunity_RefusesEveryEffectCarryingTheTag()
        {
            GameObject source = NewBareActor("Source");
            GameObject target = NewActor("Target", Health, 100f);
            GameplayEffectDefinition ward = NewEffect(
                GameplayEffectDurationPolicy.Duration, null, 2f);
            ward.SetTagsForTests(immunity: new[] { FireEffect });
            GameplayEffectDefinition fire = NewEffect(
                GameplayEffectDurationPolicy.Instant, new[] { Add(Health, -10f) });
            fire.SetTagsForTests(effect: new[] { FireEffect });
            GameplayEffectContainer container = Effects(target);

            Apply(ward, source, target);
            Assert.IsTrue(container.IsImmuneTo(fire));
            Assert.AreEqual(
                GameplayEffectApplicationOutcome.Blocked,
                Apply(fire, source, target).Outcome);
            Assert.AreEqual(100f, Attributes(target).GetBase(Health));

            container.Tick(2f);

            Assert.IsFalse(container.IsImmuneTo(fire));
            Apply(fire, source, target);
            Assert.AreEqual(90f, Attributes(target).GetBase(Health));
        }

        [Test]
        public void RemovalTags_ClearMatchingEffectsOnApplication()
        {
            GameObject source = NewBareActor("Source");
            GameObject target = NewActor("Target", Health, 50f);
            GameplayEffectDefinition burn = NewEffect(
                GameplayEffectDurationPolicy.Infinite, new[] { Add(Health, -10f) });
            burn.SetTagsForTests(effect: new[] { FireEffect }, granted: new[] { Burning });
            GameplayEffectDefinition douse = NewEffect(GameplayEffectDurationPolicy.Instant);
            douse.SetTagsForTests(removal: new[] { FireEffect });
            GameplayEffectContainer container = Effects(target);

            GameplayEffectHandle handle = Apply(burn, source, target).Handle;
            Assert.AreEqual(40f, Attributes(target).GetCurrent(Health));

            Apply(douse, source, target);

            Assert.IsFalse(container.IsActive(handle));
            Assert.IsFalse(container.HasEffectTag(Burning));
            Assert.AreEqual(50f, Attributes(target).GetCurrent(Health));
        }

        [Test]
        public void RemoveEffectsFromSource_ClearsOnlyThatAttacker()
        {
            GameObject first = NewBareActor("SourceA");
            GameObject second = NewBareActor("SourceB");
            GameObject target = NewActor("Target", Health, 10f, 500f);
            GameplayEffectDefinition effect = NewEffect(
                GameplayEffectDurationPolicy.Infinite, new[] { Add(Health, 20f) });
            GameplayEffectContainer container = Effects(target);

            Apply(effect, first, target);
            Apply(effect, second, target);
            Assert.AreEqual(50f, Attributes(target).GetCurrent(Health));

            Assert.AreEqual(1, container.RemoveEffectsFromSource(first));

            Assert.AreEqual(30f, Attributes(target).GetCurrent(Health));
        }

        [Test]
        public void SpecGrantedTag_IsAppliedOnTopOfTheAuthoredOnes()
        {
            GameObject source = NewBareActor("Source");
            GameObject target = NewActor("Target", Health, 50f);
            GameplayEffectDefinition effect = NewEffect(GameplayEffectDurationPolicy.Infinite);
            effect.SetTagsForTests(granted: new[] { Burning });
            GameplayEffectContainer container = Effects(target);

            container.Apply(new GameplayEffectSpec(effect, source, target).AddGrantedTag(Marked));

            Assert.IsTrue(container.HasEffectTag(Burning));
            Assert.IsTrue(container.HasEffectTag(Marked));
            Assert.AreEqual(1, effect.GrantedTags.Count,
                "The per-application tag never reaches the asset.");
        }
    }
}
