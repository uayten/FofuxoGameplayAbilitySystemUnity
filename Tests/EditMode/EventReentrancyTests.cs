using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    /// <summary>
    /// What a consumer's event handler may legally call back into. This is the
    /// contract a boss brain relies on without knowing it: "when the swing ends,
    /// start the next one" is a re-entrant activation raised from inside the
    /// system's own teardown, and whether it is safe was never written down or
    /// tested.
    ///
    /// The rule the tests below pin, and which the README states: **an
    /// activation is fully started before <c>AbilityStarted</c> and fully ended
    /// before <c>AbilityCompleted</c> or <c>AbilityCancelled</c>**, so a handler
    /// never sees a half-built one and may activate, cancel and query freely.
    /// The two timeline events — <c>StepTransitioned</c> and
    /// <c>GameplayCueTriggered</c> — run inside the tick, where the system
    /// re-checks that an instance is still active after every handler.
    /// </summary>
    public sealed class EventReentrancyTests
    {
        private const float Tick = 1f / 60f;
        private static readonly GameplayTag Attacking = new("State.Test.Attacking");

        private readonly List<Object> owned = new();

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

        // ------------------------------------------------------ the end events

        [Test]
        public void CompletedMayStartTheNextAbility()
        {
            TimelineAbilityDefinition first = NewAbility("test.reentry.first");
            TimelineAbilityDefinition second = NewAbility("test.reentry.second");
            AbilitySystem system = NewSystem(out GameObject owner, first, second);

            bool chained = false;
            int runningInsideHandler = -1;
            system.AbilityCompleted += ended =>
            {
                if (ended != first)
                {
                    return;
                }

                chained = system.TryActivate(second, AbilityContext.FromTarget(owner, null));
                runningInsideHandler = system.ActiveAbilityCount;
            };

            Assert.IsTrue(system.TryActivate(first, AbilityContext.FromTarget(owner, null)));
            RunToEnd(system);

            Assert.IsTrue(
                chained,
                "the one-ability-at-a-time default would have refused a replacement " +
                "started while the first was still in the list");
            Assert.AreEqual(1, runningInsideHandler, "and only the replacement was running");
        }

        /// <summary>
        /// The one-ability-at-a-time default would refuse a replacement started
        /// while the first was still in the list. It does not, which is the
        /// proof that the ending activation is gone before the handler runs.
        /// </summary>
        [Test]
        public void CancelledMayRestartTheSameAbility()
        {
            TimelineAbilityDefinition ability = NewAbility("test.reentry.restart");
            AbilitySystem system = NewSystem(out GameObject owner, ability);

            int restarts = 0;
            system.AbilityCancelled += (_, _) =>
            {
                if (restarts++ == 0)
                {
                    system.TryActivate(ability, AbilityContext.FromTarget(owner, null));
                }
            };

            Assert.IsTrue(system.TryActivate(ability, AbilityContext.FromTarget(owner, null)));
            Assert.IsTrue(system.TryCancelActiveAbility(CommonGameplayTags.CancelManual));

            Assert.AreEqual(ability, system.ActiveAbility);
            Assert.AreEqual(1, system.ActiveAbilityCount);
            Assert.AreEqual(1, restarts, "the restart did not re-enter the handler");
        }

        [Test]
        public void TheEndedAbilitysTagsAreAlreadyReleasedWhenTheHandlerRuns()
        {
            TimelineAbilityDefinition ability = NewAbility("test.reentry.tags");
            SetField(ability, "grantedTags", new[] { Attacking });
            AbilitySystem system = NewSystem(out GameObject owner, ability);

            bool heldDuringHandler = true;
            system.AbilityCancelled += (_, _) => heldDuringHandler = system.HasTag(Attacking);

            Assert.IsTrue(system.TryActivate(ability, AbilityContext.FromTarget(owner, null)));
            system.TryCancelActiveAbility(CommonGameplayTags.CancelManual);

            Assert.IsFalse(
                heldDuringHandler,
                "a handler sees the world after the ending, never mid-teardown");
        }

        // ---------------------------------------------------- the start event

        [Test]
        public void StartedSeesTheActivationAlreadyRunning()
        {
            TimelineAbilityDefinition ability = NewAbility("test.reentry.started");
            SetField(ability, "grantedTags", new[] { Attacking });
            AbilitySystem system = NewSystem(out GameObject owner, ability);

            AbilityDefinition seen = null;
            bool tagHeld = false;
            system.AbilityStarted += started =>
            {
                seen = system.ActiveAbility;
                tagHeld = system.HasTag(Attacking);
            };

            Assert.IsTrue(system.TryActivate(ability, AbilityContext.FromTarget(owner, null)));

            Assert.AreEqual(ability, seen, "the instance is in the list before the event");
            Assert.IsTrue(tagHeld, "and its granted tags are already held");
        }

        [Test]
        public void StartedMayCancelTheActivationItJustSaw()
        {
            TimelineAbilityDefinition ability = NewAbility("test.reentry.startedcancel");
            SetField(ability, "grantedTags", new[] { Attacking });
            AbilitySystem system = NewSystem(out GameObject owner, ability);

            system.AbilityStarted += _ =>
                system.TryCancelActiveAbility(CommonGameplayTags.CancelManual);

            system.TryActivate(ability, AbilityContext.FromTarget(owner, null));

            Assert.AreEqual(0, system.ActiveAbilityCount);
            Assert.IsFalse(system.HasTag(Attacking));
        }

        // ------------------------------------------------- the timeline events

        [Test]
        public void ACueHandlerMayCancelTheRunningActivation()
        {
            TimelineAbilityDefinition ability = NewAbility("test.reentry.cue");
            SetField(ability, "grantedTags", new[] { Attacking });
            ability.FirstStepForTests.SetCueTriggersForTests(new[]
            {
                new GameplayCueTrigger(1, new GameplayTag("Cue.Test.Impact")),
            });
            AbilitySystem system = NewSystem(out GameObject owner, ability);

            system.GameplayCueTriggered += _ =>
                system.TryCancelActiveAbility(CommonGameplayTags.CancelManual);

            Assert.IsTrue(system.TryActivate(ability, AbilityContext.FromTarget(owner, null)));
            system.Tick(Tick);
            system.Tick(Tick);

            Assert.AreEqual(0, system.ActiveAbilityCount);
            Assert.IsFalse(system.HasTag(Attacking));
        }

        [Test]
        public void AStepTransitionHandlerMayCancelTheRunningActivation()
        {
            TimelineAbilityDefinition combo = NewAbility("test.reentry.transition", steps: 3);
            SetField(combo, "grantedTags", new[] { Attacking });
            AbilitySystem system = NewSystem(out GameObject owner, combo);

            system.StepTransitioned += transition =>
            {
                if (transition.ToStepIndex >= 1)
                {
                    system.TryCancelActiveAbility(CommonGameplayTags.CancelManual);
                }
            };

            Assert.IsTrue(system.TryActivate(combo, AbilityContext.FromTarget(owner, null)));
            RunToEnd(system);

            Assert.AreEqual(0, system.ActiveAbilityCount);
            Assert.IsFalse(system.HasTag(Attacking));
        }

        /// <summary>
        /// The tick iterates a copy and re-checks that each instance is still
        /// active, so a handler that ends one activation cannot corrupt the
        /// iteration over the others.
        /// </summary>
        [Test]
        public void AHandlerEndingOneActivationLeavesTheOthersTicking()
        {
            GameplayTag attacks = new("Group.Test.Attacks");
            GameplayTag auras = new("Group.Test.Auras");

            TimelineAbilityDefinition attack = NewAbility("test.reentry.concurrent.attack", steps: 3);
            attack.SetExclusionGroupForTests(
                attacks, AbilityGroupExclusionPolicy.BlockWhileSameGroupActive);
            TimelineAbilityDefinition aura = NewAbility("test.reentry.concurrent.aura", steps: 3);
            aura.SetExclusionGroupForTests(
                auras, AbilityGroupExclusionPolicy.BlockWhileSameGroupActive);
            AbilitySystem system = NewSystem(out GameObject owner, attack, aura);

            system.StepTransitioned += transition =>
            {
                if (transition.Ability == attack && transition.ToStepIndex >= 1)
                {
                    system.TryCancelAbility(attack, CommonGameplayTags.CancelManual);
                }
            };

            Assert.IsTrue(system.TryActivate(attack, AbilityContext.FromTarget(owner, null)));
            Assert.IsTrue(system.TryActivate(aura, AbilityContext.FromTarget(owner, null)));

            for (int i = 0; i < 8 && system.ActiveAbilityCount == 2; i++)
            {
                system.Tick(Tick);
            }

            Assert.AreEqual(1, system.ActiveAbilityCount);
            Assert.AreEqual(aura, system.ActiveAbility, "the aura kept its own timeline");
        }

        // ------------------------------------------------------------ helpers

        private static void RunToEnd(AbilitySystem system)
        {
            for (int i = 0; i < 240 && system.ActiveAbilityCount > 0; i++)
            {
                system.Tick(Tick);
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
            out GameObject owner, params AbilityDefinition[] abilities)
        {
            owner = Own(new GameObject("ReentrancyOwner"));
            AbilityLoadout loadout = Own(ScriptableObject.CreateInstance<AbilityLoadout>());
            SetField(loadout, "abilities", abilities);
            AbilitySystem system = owner.AddComponent<AbilitySystem>();
            SetField(system, "loadout", loadout);
            return system;
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
