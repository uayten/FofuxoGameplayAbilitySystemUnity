using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    public sealed class GameplayEventTriggerTests
    {
        private readonly List<UnityEngine.Object> owned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (UnityEngine.Object item in owned)
            {
                if (item != null)
                {
                    UnityEngine.Object.DestroyImmediate(item);
                }
            }

            owned.Clear();
        }

        [Test]
        public void GameplayEvent_ActivatesMatchingGrantedAbilityAndGrantsTags()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition reaction = NewAbility("test.physics.reaction");
            reaction.SetActivationTriggersForTests(
                new AbilityActivationTrigger(CommonGameplayTags.PhysicsForceEvent, true));
            SetField(reaction, "grantedTags", new[]
            {
                CommonGameplayTags.PhysicsControlled,
                CommonGameplayTags.MovementLocked,
            });
            Grant(system, reaction);

            bool handled = system.TryHandleGameplayEvent(
                CommonGameplayTags.PhysicsForceEvent,
                AbilityContext.FromDirection(owner, null, Vector3.forward),
                out AbilityDefinition triggered);

            Assert.IsTrue(handled);
            Assert.AreSame(reaction, triggered);
            Assert.AreSame(reaction, system.ActiveAbility);
            Assert.IsTrue(system.HasTag(CommonGameplayTags.PhysicsControlled));
            Assert.IsTrue(system.HasTag(CommonGameplayTags.MovementLocked));
        }

        [Test]
        public void InterruptingGameplayEvent_CancelsTheRunningAbilityAsSuperseded()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition action = NewAbility("test.action");
            TimelineAbilityDefinition reaction = NewAbility("test.physics.reaction");
            reaction.SetActivationTriggersForTests(
                new AbilityActivationTrigger(CommonGameplayTags.PhysicsForceEvent, true));
            Grant(system, action, reaction);

            GameplayTag cancellation = default;
            system.AbilityCancelled += (_, cancelTag) => cancellation = cancelTag;
            Assert.IsTrue(system.TryActivate(action, AbilityContext.FromTarget(owner, null)));

            Assert.IsTrue(system.TryHandleGameplayEvent(
                CommonGameplayTags.PhysicsForceEvent,
                AbilityContext.FromDirection(owner, null, Vector3.right),
                out _));

            Assert.AreEqual(CommonGameplayTags.CancelSuperseded, cancellation);
            Assert.AreSame(reaction, system.ActiveAbility);
        }

        [Test]
        public void RepeatedGameplayEvent_DoesNotRestartTheReactionAbility()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition reaction = NewAbility("test.physics.repeated");
            reaction.SetActivationTriggersForTests(
                new AbilityActivationTrigger(CommonGameplayTags.PhysicsForceEvent, true));
            Grant(system, reaction);

            int startCount = 0;
            system.AbilityStarted += _ => startCount++;
            AbilityContext context = AbilityContext.FromDirection(
                owner,
                null,
                Vector3.forward);

            Assert.IsTrue(system.TryHandleGameplayEvent(
                CommonGameplayTags.PhysicsForceEvent,
                context,
                out _));
            system.Tick(0.05f);
            int frameBeforeRepeat = system.ActiveFrame;
            Assert.IsTrue(system.TryHandleGameplayEvent(
                CommonGameplayTags.PhysicsForceEvent,
                context,
                out _));

            Assert.AreEqual(1, startCount);
            Assert.AreEqual(frameBeforeRepeat, system.ActiveFrame);
        }

        [Test]
        public void ExternalCompletion_RemovesReactionTags()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition reaction = NewAbility("test.physics.complete");
            reaction.SetActivationTriggersForTests(
                new AbilityActivationTrigger(CommonGameplayTags.PhysicsForceEvent, true));
            SetField(reaction, "grantedTags", new[] { CommonGameplayTags.PhysicsControlled });
            Grant(system, reaction);

            system.TryHandleGameplayEvent(
                CommonGameplayTags.PhysicsForceEvent,
                AbilityContext.FromTarget(owner, null),
                out _);

            Assert.IsTrue(system.TryCompleteActiveAbility(reaction));
            Assert.IsFalse(system.IsActive);
            Assert.IsFalse(system.HasTag(CommonGameplayTags.PhysicsControlled));
            Assert.IsFalse(system.TryCompleteActiveAbility(reaction));
        }

        [Test]
        public void MissingSerializedTriggers_AreTreatedAsEmpty()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition legacy = NewAbility("test.legacy");
            SetField<AbilityActivationTrigger[]>(legacy, "activationTriggers", null);
            Grant(system, legacy);

            Assert.IsTrue(legacy.TryValidate(out string error), error);
            Assert.IsFalse(system.TryHandleGameplayEvent(
                CommonGameplayTags.PhysicsForceEvent,
                AbilityContext.FromTarget(owner, null),
                out _));
        }

        [Test]
        public void EmptyActivationTriggerTag_FailsValidation()
        {
            TimelineAbilityDefinition reaction = NewAbility("test.physics.invalid");
            reaction.SetActivationTriggersForTests(new AbilityActivationTrigger(default, true));

            Assert.IsFalse(reaction.TryValidate(out string error));
            StringAssert.Contains("Activation trigger", error);
        }

        private AbilitySystem NewSystem(out GameObject owner)
        {
            owner = new GameObject("GameplayEventOwner");
            owned.Add(owner);
            return owner.AddComponent<AbilitySystem>();
        }

        private TimelineAbilityDefinition NewAbility(string id)
        {
            TimelineAbilityDefinition ability = ScriptableObject.CreateInstance<TimelineAbilityDefinition>();
            owned.Add(ability);
            ability.SetAbilityIdForTests(id);
            SetField(ability, "requiresTarget", false);
            var step = new AbilityStep();
            step.ConfigureForTests(1, 2, 600, 60f);
            ability.SetStepsForTests(step);
            return ability;
        }

        private void Grant(AbilitySystem system, params AbilityDefinition[] abilities)
        {
            AbilityLoadout loadout = ScriptableObject.CreateInstance<AbilityLoadout>();
            owned.Add(loadout);
            SetField(loadout, "abilities", abilities);
            SetField(system, "loadout", loadout);
        }

        private static void SetField<TValue>(object target, string name, TValue value)
        {
            // Walks up the hierarchy: a private field of a base class is
            // invisible to a single GetField call, and an ability's own fields
            // sit one level above the timeline type most fixtures use.
            for (System.Type type = target.GetType(); type != null; type = type.BaseType)
            {
                System.Reflection.FieldInfo field = type.GetField(
                    name,
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.DeclaredOnly);
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
