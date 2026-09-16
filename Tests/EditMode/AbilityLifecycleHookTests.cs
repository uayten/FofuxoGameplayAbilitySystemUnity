using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    /// <summary>
    /// A derived ability is the extension point for behaviour that data cannot
    /// express. These tests pin the contract that makes one usable: the hooks
    /// fire, in order, and before the matching public event.
    /// </summary>
    public sealed class AbilityLifecycleHookTests
    {
        private sealed class RecordingAbility : TimelineAbilityDefinition
        {
            public readonly List<string> Calls = new();

            protected internal override void OnActivated(AbilityInstance instance)
            {
                Calls.Add($"activated:{instance.Definition.AbilityId}");
            }

            protected internal override void OnStepStarted(
                AbilityInstance instance,
                int stepIndex)
            {
                Calls.Add($"step:{stepIndex}");
            }

            protected internal override void OnCompleted(AbilityInstance instance)
            {
                Calls.Add("completed");
            }

            protected internal override void OnCancelled(
                AbilityInstance instance,
                GameplayTag cancelTag)
            {
                Calls.Add($"cancelled:{cancelTag}");
            }
        }

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

        [Test]
        public void ActivationCallsActivatedThenTheFirstStep()
        {
            RecordingAbility ability = NewAbility("test.hooks.activate", 1);
            AbilitySystem system = NewSystem(ability, out GameObject owner);

            Assert.IsTrue(system.TryActivate(
                ability, AbilityContext.FromTarget(owner, null)));

            CollectionAssert.AreEqual(
                new[] { "activated:test.hooks.activate", "step:0" },
                ability.Calls);
        }

        [Test]
        public void EveryStepOfAComboReportsItsOwnIndex()
        {
            RecordingAbility ability = NewAbility("test.hooks.combo", 2);
            AbilitySystem system = NewSystem(ability, out GameObject owner);

            Assert.IsTrue(system.TryActivate(
                ability, AbilityContext.FromTarget(owner, null)));
            for (int i = 0; i < 4; i++)
            {
                system.Tick(1f / 60f);
            }

            CollectionAssert.Contains(ability.Calls, "step:1");
            Assert.AreEqual(1, system.ActiveStepIndex);
        }

        [Test]
        public void CancellationCarriesTheRequestTagAndPrecedesTheEvent()
        {
            RecordingAbility ability = NewAbility("test.hooks.cancel", 1);
            AbilitySystem system = NewSystem(ability, out GameObject owner);
            // Both land in one list, so their order is the assertion.
            system.AbilityCancelled += (_, _) => ability.Calls.Add("event");

            Assert.IsTrue(system.TryActivate(
                ability, AbilityContext.FromTarget(owner, null)));
            system.ForceCancelActiveAbility(CommonGameplayTags.CancelManual);

            CollectionAssert.AreEqual(
                new[]
                {
                    "activated:test.hooks.cancel",
                    "step:0",
                    "cancelled:Cancel.Manual",
                    "event",
                },
                ability.Calls);
        }

        [Test]
        public void CompletionFiresTheCompletedHookOnce()
        {
            RecordingAbility ability = NewAbility("test.hooks.complete", 1);
            AbilitySystem system = NewSystem(ability, out GameObject owner);

            Assert.IsTrue(system.TryActivate(
                ability, AbilityContext.FromTarget(owner, null)));
            for (int i = 0; i < 6; i++)
            {
                system.Tick(1f / 60f);
            }

            Assert.IsFalse(system.IsActive);
            Assert.AreEqual(1, ability.Calls.FindAll(call => call == "completed").Count);
        }

        private RecordingAbility NewAbility(string id, int stepCount)
        {
            RecordingAbility ability = ScriptableObject.CreateInstance<RecordingAbility>();
            owned.Add(ability);
            ability.SetAbilityIdForTests(id);
            SetField(ability, "requiresTarget", false);
            AbilityStep[] steps = new AbilityStep[stepCount];
            for (int i = 0; i < stepCount; i++)
            {
                steps[i] = new AbilityStep();
                steps[i].ConfigureForTests(1, 2, 3, 60f);
            }

            ability.SetStepsForTests(steps);
            return ability;
        }

        private AbilitySystem NewSystem(AbilityDefinition ability, out GameObject owner)
        {
            owner = new GameObject("HookOwner");
            owned.Add(owner);
            AbilityLoadout loadout = ScriptableObject.CreateInstance<AbilityLoadout>();
            owned.Add(loadout);
            SetField(loadout, "abilities", new[] { ability });
            AbilitySystem system = owner.AddComponent<AbilitySystem>();
            SetField(system, "loadout", loadout);
            return system;
        }

        /// <summary>
        /// Walks up the hierarchy: a derived ability's private base fields are
        /// invisible to a single GetField call, which is exactly the case here.
        /// </summary>
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
