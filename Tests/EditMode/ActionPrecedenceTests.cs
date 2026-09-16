using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    /// <summary>
    /// Which action beats which. Every knob used here — exclusion groups, cancel
    /// policies, blocked tags — is covered on its own elsewhere; what was never
    /// pinned is how they compose, and the composition is what a consumer
    /// actually depends on. "Can I roll out of this swing?" is not a question
    /// about one policy.
    ///
    /// The five actions are authored the way BossRush authors them, and the
    /// order they produce is:
    ///
    /// <code>
    /// death  &gt;  hit reaction  &gt;  roll  &gt;  attack, block
    /// </code>
    ///
    /// with attack and block mutually exclusive rather than ordered — neither
    /// interrupts the other. The hit reaction stands in for the target-owned
    /// reaction ability that Milestone 3 will formalise; it is authored here the
    /// way the physics reaction already is, as an event-triggered ability that
    /// cancels whatever is running.
    /// </summary>
    public sealed class ActionPrecedenceTests
    {
        private const float Tick = 1f / 60f;

        private static readonly GameplayTag Action = new("Group.Test.Action");
        private static readonly GameplayTag HitEvent = new("Event.Test.Hit");
        private static readonly GameplayTag Dead = CommonGameplayTags.Dead;
        private static readonly GameplayTag Invulnerable = CommonGameplayTags.Invulnerable;

        private AbilityDefinition attack;
        private AbilityDefinition block;
        private AbilityDefinition roll;
        private AbilityDefinition reaction;
        private AbilityDefinition death;
        private AbilitySystem system;
        private GameObject owner;

        private readonly List<Object> owned = new();

        [SetUp]
        public void SetUp()
        {
            // Attack and block share the action group and refuse to interrupt
            // each other: one committed action at a time.
            attack = NewAbility("test.precedence.attack", steps: 3);
            attack.SetExclusionGroupForTests(
                Action, AbilityGroupExclusionPolicy.BlockWhileSameGroupActive);
            SetField(attack, "blockedTags", new[] { Dead });

            block = NewAbility("test.precedence.block");
            block.SetExclusionGroupForTests(
                Action, AbilityGroupExclusionPolicy.BlockWhileSameGroupActive);
            SetField(block, "blockedTags", new[] { Dead });

            // The roll takes the group from whatever holds it, and grants
            // i-frames while it runs.
            roll = NewAbility("test.precedence.roll", steps: 2);
            roll.SetExclusionGroupForTests(
                Action, AbilityGroupExclusionPolicy.CancelSameGroup);
            SetField(roll, "blockedTags", new[] { Dead });
            SetField(roll, "grantedTags", new[] { Invulnerable });

            // The reaction is event-driven and outranks every action — except
            // that an invulnerable owner was never hit, so it is refused there.
            reaction = NewAbility("test.precedence.reaction");
            reaction.SetExclusionGroupForTests(
                default, AbilityGroupExclusionPolicy.CancelAnyActive);
            reaction.SetActivationTriggersForTests(
                new AbilityActivationTrigger(HitEvent, true));
            SetField(reaction, "blockedTags", new[] { Dead, Invulnerable });

            // Death outranks everything and refuses to be interrupted by
            // anything at all.
            death = NewAbility("test.precedence.death", steps: 4);
            death.SetExclusionGroupForTests(
                default, AbilityGroupExclusionPolicy.CancelAnyActive);
            death.SetCancellationForTests(AbilityCancelPolicy.Nothing);
            SetField(death, "grantedTags", new[] { Dead });

            system = NewSystem(out owner, attack, block, roll, reaction, death);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object item in owned)
            {
                if (item != null)
                {
                    Object.DestroyImmediate(item);
                }
            }

            owned.Clear();
        }

        // ------------------------------------------------- attack versus block

        [Test]
        public void BlockDoesNotInterruptAnAttack()
        {
            Assert.IsTrue(Activate(attack));

            Assert.IsFalse(Activate(block, out AbilityActivationResult result));
            Assert.AreEqual(AbilityActivationRejection.BlockedByGroup, result.Rejection);
            Assert.AreEqual(attack, system.ActiveAbility);
        }

        [Test]
        public void AnAttackDoesNotInterruptABlock()
        {
            Assert.IsTrue(Activate(block));

            Assert.IsFalse(Activate(attack, out AbilityActivationResult result));
            Assert.AreEqual(AbilityActivationRejection.BlockedByGroup, result.Rejection);
            Assert.AreEqual(block, system.ActiveAbility);
        }

        // -------------------------------------------------------- roll wins

        [Test]
        public void ARollCancelsAnAttack()
        {
            Assert.IsTrue(Activate(attack));

            Assert.IsTrue(Activate(roll));

            Assert.AreEqual(roll, system.ActiveAbility);
            Assert.AreEqual(1, system.ActiveAbilityCount);
        }

        [Test]
        public void ARollCancelsABlock()
        {
            Assert.IsTrue(Activate(block));

            Assert.IsTrue(Activate(roll));

            Assert.AreEqual(roll, system.ActiveAbility);
        }

        [Test]
        public void AnAttackIsRefusedDuringARoll()
        {
            Assert.IsTrue(Activate(roll));

            Assert.IsFalse(Activate(attack, out AbilityActivationResult result));
            Assert.AreEqual(AbilityActivationRejection.BlockedByGroup, result.Rejection);
            Assert.AreEqual(roll, system.ActiveAbility);
        }

        // ------------------------------------------------- the hit reaction

        [Test]
        public void AHitReactionCancelsAnAttack()
        {
            Assert.IsTrue(Activate(attack));

            Assert.IsTrue(system.TryHandleGameplayEvent(HitEvent, Context(), out _));

            Assert.AreEqual(reaction, system.ActiveAbility);
            Assert.AreEqual(1, system.ActiveAbilityCount);
        }

        [Test]
        public void AHitReactionCancelsABlock()
        {
            Assert.IsTrue(Activate(block));

            Assert.IsTrue(system.TryHandleGameplayEvent(HitEvent, Context(), out _));

            Assert.AreEqual(reaction, system.ActiveAbility);
        }

        /// <summary>
        /// The decision belongs to the target: an owner mid-roll holds
        /// <c>State.Invulnerable</c>, so the reaction is refused and the roll
        /// keeps running. This is the precedence the whole i-frame system rests
        /// on, and it is expressed by a blocked tag rather than by the group.
        /// </summary>
        [Test]
        public void AHitReactionIsRefusedWhileTheOwnerIsInvulnerable()
        {
            Assert.IsTrue(Activate(roll));
            Assert.IsTrue(system.HasTag(Invulnerable));

            Assert.IsFalse(system.TryHandleGameplayEvent(HitEvent, Context(), out _));

            Assert.AreEqual(roll, system.ActiveAbility, "i-frames outrank the reaction");
        }

        [Test]
        public void AHitReactionCancelsARollThatIsNoLongerInvulnerable()
        {
            TimelineAbilityDefinition plainRoll = NewAbility("test.precedence.roll.plain", steps: 2);
            plainRoll.SetExclusionGroupForTests(
                Action, AbilityGroupExclusionPolicy.CancelSameGroup);
            system = NewSystem(out owner, plainRoll, reaction);

            Assert.IsTrue(Activate(plainRoll));
            Assert.IsFalse(system.HasTag(Invulnerable));

            Assert.IsTrue(system.TryHandleGameplayEvent(HitEvent, Context(), out _));
            Assert.AreEqual(reaction, system.ActiveAbility);
        }

        // -------------------------------------------------------- death wins

        [Test]
        public void DeathCancelsWhateverIsRunning()
        {
            Assert.IsTrue(Activate(attack));

            Assert.IsTrue(Activate(death));

            Assert.AreEqual(death, system.ActiveAbility);
            Assert.AreEqual(1, system.ActiveAbilityCount);
        }

        [Test]
        public void DeathCancelsARollThroughItsIFrames()
        {
            Assert.IsTrue(Activate(roll));

            Assert.IsTrue(Activate(death));

            Assert.AreEqual(death, system.ActiveAbility);
            Assert.IsFalse(system.HasTag(Invulnerable), "the roll took its i-frames with it");
        }

        [Test]
        public void DeathCancelsAHitReaction()
        {
            Assert.IsTrue(system.TryHandleGameplayEvent(HitEvent, Context(), out _));
            Assert.AreEqual(reaction, system.ActiveAbility);

            Assert.IsTrue(Activate(death));

            Assert.AreEqual(death, system.ActiveAbility);
        }

        [Test]
        public void NothingInterruptsDeath()
        {
            Assert.IsTrue(Activate(death));

            foreach (AbilityDefinition ability in new[] { attack, block, roll })
            {
                Assert.IsFalse(
                    Activate(ability, out AbilityActivationResult result),
                    $"{ability.AbilityId} must not interrupt death");
                Assert.AreNotEqual(AbilityActivationRejection.None, result.Rejection);
            }

            Assert.IsFalse(system.TryHandleGameplayEvent(HitEvent, Context(), out _));
            Assert.AreEqual(death, system.ActiveAbility);
        }

        /// <summary>
        /// A dead owner refuses a fresh action even once the death ability has
        /// ended, because the loose tag survives it. This is the difference
        /// between "death is running" and "the owner is dead".
        /// </summary>
        [Test]
        public void ADeadOwnerStartsNothingAfterTheDeathAbilityEnds()
        {
            system.SetLooseTag(Dead, true);

            foreach (AbilityDefinition ability in new[] { attack, block, roll, reaction })
            {
                Assert.IsFalse(
                    Activate(ability, out AbilityActivationResult result),
                    $"{ability.AbilityId} must not start on a dead owner");
                Assert.AreEqual(AbilityActivationRejection.BlockedByTag, result.Rejection);
            }
        }

        /// <summary>
        /// The whole order in one pass, because each pair passing separately
        /// does not prove the chain holds end to end.
        /// </summary>
        [Test]
        public void TheWholeOrderHoldsInOneSequence()
        {
            Assert.IsTrue(Activate(attack), "attack starts");
            Assert.IsFalse(Activate(block), "block does not interrupt it");
            Assert.IsTrue(Activate(roll), "the roll does");
            Assert.IsFalse(
                system.TryHandleGameplayEvent(HitEvent, Context(), out _),
                "and its i-frames refuse the reaction");

            RunToEnd(system);
            Assert.IsFalse(system.HasTag(Invulnerable));

            Assert.IsTrue(Activate(attack), "attack starts again");
            Assert.IsTrue(
                system.TryHandleGameplayEvent(HitEvent, Context(), out _),
                "the reaction cancels it now");
            Assert.IsTrue(Activate(death), "and death cancels the reaction");
            Assert.IsFalse(Activate(attack), "after which nothing starts");

            Assert.AreEqual(death, system.ActiveAbility);
        }

        // ------------------------------------------------------------ helpers

        private AbilityContext Context() => AbilityContext.FromTarget(owner, null);

        private bool Activate(AbilityDefinition ability) =>
            system.TryActivate(ability, Context());

        private bool Activate(AbilityDefinition ability, out AbilityActivationResult result) =>
            system.TryActivate(ability, Context(), out result);

        private static void RunToEnd(AbilitySystem target)
        {
            for (int i = 0; i < 240 && target.ActiveAbilityCount > 0; i++)
            {
                target.Tick(Tick);
            }
        }

        private T Own<T>(T item) where T : Object
        {
            owned.Add(item);
            return item;
        }

        private TimelineAbilityDefinition NewAbility(string abilityId, int steps = 1)
        {
            TimelineAbilityDefinition ability = Own(ScriptableObject.CreateInstance<TimelineAbilityDefinition>());
            ability.SetAbilityIdForTests(abilityId);
            SetField(ability, "requiresTarget", false);
            AbilityStep[] built = new AbilityStep[steps];
            for (int i = 0; i < steps; i++)
            {
                built[i] = new AbilityStep();
                built[i].ConfigureForTests(1, 2, 3, 60f);
            }

            ability.SetStepsForTests(built);
            return ability;
        }

        private AbilitySystem NewSystem(
            out GameObject systemOwner, params AbilityDefinition[] abilities)
        {
            systemOwner = Own(new GameObject("PrecedenceOwner"));
            AbilityLoadout loadout = Own(ScriptableObject.CreateInstance<AbilityLoadout>());
            SetField(loadout, "abilities", abilities);
            AbilitySystem built = systemOwner.AddComponent<AbilitySystem>();
            SetField(built, "loadout", loadout);
            return built;
        }

        private static void SetField<TValue>(object target, string name, TValue value)
        {
            for (System.Type type = target.GetType(); type != null; type = type.BaseType)
            {
                FieldInfo field = type.GetField(
                    name,
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                {
                    field.SetValue(target, value);
                    return;
                }
            }

            Assert.Fail($"No field named {name}.");
        }
    }
}
