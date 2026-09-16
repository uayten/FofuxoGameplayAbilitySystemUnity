using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    /// <summary>
    /// The trail an actor leaves. The entry that justifies the whole ring is
    /// the refused activation: before this, an activation that was turned down
    /// left nothing behind at all.
    /// </summary>
    public sealed class AbilityEventHistoryTests
    {
        private static readonly GameplayAttribute Health = new("Test.Health");
        private static readonly GameplayTag Warded = new("State.Warded");

        private GameObject owner;
        private GameObject target;
        private AbilitySystem system;
        private readonly List<Object> owned = new();
        private bool previousEnabled;
        private int previousCapacity;

        [SetUp]
        public void SetUp()
        {
            previousEnabled = AbilityDiagnostics.Enabled;
            previousCapacity = AbilityDiagnostics.HistoryCapacity;
            AbilityDiagnostics.Enabled = true;
            owner = new GameObject("HistoryOwner");
            target = new GameObject("HistoryTarget");
            target.transform.position = owner.transform.position + Vector3.forward;
            system = owner.AddComponent<AbilitySystem>();
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
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(owner);
            AbilityDiagnostics.Enabled = previousEnabled;
            AbilityDiagnostics.HistoryCapacity = previousCapacity;
        }

        // ------------------------------------------------------------- ring

        [Test]
        public void Ring_KeepsTheNewestEntries_OldestFirst()
        {
            AbilityEventHistory history = new(3);
            AbilityDefinition ability = NewAbility("test.ring");

            for (int i = 1; i <= 5; i++)
            {
                history.Record(Rejected(ability, i.ToString()));
            }

            Assert.AreEqual(3, history.Capacity);
            Assert.AreEqual(3, history.Count);
            Assert.AreEqual(5, history.TotalRecorded);
            Assert.AreEqual(2, history.DroppedCount);
            Assert.AreEqual("3", history[0].Message);
            Assert.AreEqual("4", history[1].Message);
            Assert.AreEqual("5", history[2].Message);
            Assert.AreEqual("5", history.Latest.Message);
        }

        [Test]
        public void Ring_RaisesRecorded_AndClears()
        {
            AbilityEventHistory history = new(4);
            AbilityDefinition ability = NewAbility("test.ring");
            int raised = 0;
            history.Recorded += _ => raised++;

            history.Record(Rejected(ability, "1"));
            history.Record(Rejected(ability, "2"));
            history.Clear();

            Assert.AreEqual(2, raised);
            Assert.AreEqual(0, history.Count);
            Assert.AreEqual(2, history.TotalRecorded, "clearing forgets entries, not the tally");
            history.Record(Rejected(ability, "3"));
            Assert.AreEqual("3", history[0].Message);
        }

        [Test]
        public void Ring_CopiesNewestFirst_OnRequest()
        {
            AbilityEventHistory history = new(4);
            AbilityDefinition ability = NewAbility("test.ring");
            history.Record(Rejected(ability, "1"));
            history.Record(Rejected(ability, "2"));
            List<AbilityEvent> copy = new();

            Assert.AreEqual(2, history.CopyTo(copy, newestFirst: true));

            Assert.AreEqual("2", copy[0].Message);
            Assert.AreEqual("1", copy[1].Message);
        }

        [Test]
        public void Ring_RejectsAnIndexOutsideTheHeldEntries()
        {
            AbilityEventHistory history = new(2);

            Assert.Throws<System.ArgumentOutOfRangeException>(() => _ = history[0]);
        }

        // ------------------------------------------------------- activations

        [Test]
        public void RefusedActivation_IsRecordedWithItsCode()
        {
            AbilityDefinition stranger = NewAbility("test.stranger");

            Assert.IsFalse(system.TryActivate(stranger, Context()));

            Assert.IsTrue(system.HasHistory);
            AbilityEvent recorded = system.History.Latest;
            Assert.AreEqual(AbilityEventKind.ActivationRejected, recorded.Kind);
            Assert.AreEqual(AbilityActivationRejection.NotGranted, recorded.Rejection);
            Assert.AreSame(stranger, recorded.Ability);
            Assert.IsNotEmpty(recorded.Message);
            StringAssert.Contains("NotGranted", recorded.ToString());
        }

        [Test]
        public void RefusedActivationWithTargets_IsRecordedToo()
        {
            AbilityDefinition stranger = NewAbility("test.stranger");

            Assert.IsFalse(system.TryActivateWithTargets(
                stranger, Context(), new AbilityTargetData(), out AbilityActivationResult result));

            Assert.AreEqual(result.Rejection, system.History.Latest.Rejection);
        }

        /// <summary>
        /// The questions stay questions. AI scoring asks them every frame, and a
        /// history made of them would bury the one refusal that mattered.
        /// </summary>
        [Test]
        public void EvaluateActivation_LeavesNoTrace()
        {
            AbilityDefinition stranger = NewAbility("test.stranger");

            Assert.IsFalse(system.EvaluateActivation(stranger, Context()).IsAccepted);
            Assert.IsFalse(system.CanActivate(stranger, Context(), out _));

            Assert.IsFalse(system.HasHistory);
        }

        [Test]
        public void Start_IsRecordedWithItsTarget()
        {
            AbilityDefinition ability = Grant(NewAbility("test.start"));

            Assert.IsTrue(system.TryActivate(ability, AbilityContext.FromTarget(owner, target)));

            AbilityEvent started = system.History.Latest;
            Assert.AreEqual(AbilityEventKind.AbilityStarted, started.Kind);
            Assert.AreSame(ability, started.Ability);
            Assert.AreSame(target, started.Actor);
            Assert.AreEqual(0, started.StepIndex);
        }

        [Test]
        public void Cancellation_IsRecordedWithItsTag()
        {
            AbilityDefinition ability = Grant(NewAbility("test.cancelled"));
            Assert.IsTrue(system.TryActivate(ability, Context()));

            system.ForceCancelActiveAbility(CommonGameplayTags.CancelManual);

            AbilityEvent cancelled = system.History.Latest;
            Assert.AreEqual(AbilityEventKind.AbilityCancelled, cancelled.Kind);
            Assert.AreEqual(CommonGameplayTags.CancelManual, cancelled.Tag);
            Assert.AreSame(ability, cancelled.Ability);
            Assert.AreEqual(AbilityStepTransitionReason.Cancelled, cancelled.Reason);
        }

        [Test]
        public void Completion_IsRecorded()
        {
            AbilityDefinition ability = Grant(NewAbility("test.completed"));
            Assert.IsTrue(system.TryActivate(ability, Context()));

            Assert.IsTrue(system.TryCompleteActiveAbility(ability));

            AbilityEvent completed = system.History.Latest;
            Assert.AreEqual(AbilityEventKind.AbilityCompleted, completed.Kind);
            Assert.AreSame(ability, completed.Ability);
        }

        [Test]
        public void StepAdvance_IsRecordedAsATransition_ButNotTheStart()
        {
            TimelineAbilityDefinition combo = NewCombo("test.combo", 2);
            Grant(combo);
            Assert.IsTrue(system.TryActivate(combo, Context()));

            for (int frame = 0; frame < 12 && system.ActiveStepIndex == 0; frame++)
            {
                system.Tick(1f / 60f);
            }

            Assert.AreEqual(1, system.ActiveStepIndex, "the automatic combo chained");
            List<AbilityEvent> kinds = Collect(AbilityEventKind.StepTransitioned);
            Assert.AreEqual(1, kinds.Count);
            Assert.AreEqual(AbilityStepTransitionReason.AutomaticChain, kinds[0].Reason);
            Assert.AreEqual(1, kinds[0].StepIndex);
            Assert.AreEqual(1, Collect(AbilityEventKind.AbilityStarted).Count);
        }

        // ------------------------------------------------------ cues, events

        [Test]
        public void Cue_AndGameplayEvent_AreRecordedWithTheirTags()
        {
            GameplayTag cue = new("Cue.Test");
            GameplayTag eventTag = new("Event.Test");

            system.TriggerGameplayCue(cue, Context());
            Assert.AreEqual(AbilityEventKind.GameplayCue, system.History.Latest.Kind);
            Assert.AreEqual(cue, system.History.Latest.Tag);

            system.TryHandleGameplayEvent(eventTag, Context(), out _);
            Assert.AreEqual(AbilityEventKind.GameplayEvent, system.History.Latest.Kind);
            Assert.AreEqual(eventTag, system.History.Latest.Tag);
            Assert.AreSame(owner, system.History.Latest.Actor);
        }

        // ----------------------------------------------------------- effects

        [Test]
        public void EffectOnSelf_IsRecordedFromBothEnds()
        {
            AddHealth(owner);
            GameplayEffectDefinition effect = NewInstantEffect(GameplayEffectTargeting.Owner, -10f);

            Assert.AreEqual(1, system.ApplyGameplayEffect(effect, Context()));

            List<AbilityEvent> applied = Collect(AbilityEventKind.EffectApplied);
            Assert.AreEqual(1, applied.Count, "the target-side half");
            Assert.AreSame(effect, applied[0].Effect);
            Assert.AreEqual("executed", applied[0].Message);
            Assert.AreSame(owner, applied[0].Actor, "the source of the application");

            List<AbilityEvent> delivered = Collect(AbilityEventKind.EffectDelivered);
            Assert.AreEqual(1, delivered.Count, "the source-side half");
            Assert.AreSame(owner, delivered[0].Actor, "the actor it reached");
            Assert.IsEmpty(Collect(AbilityEventKind.EffectRefused));
        }

        [Test]
        public void BlockedEffect_IsRecordedAsBlocked_AndAsRefused()
        {
            AddHealth(owner);
            GameplayEffectDefinition effect = NewInstantEffect(GameplayEffectTargeting.Owner, -10f);
            effect.SetTagsForTests(blocked: new[] { Warded });
            system.SetLooseTag(Warded, true);

            Assert.AreEqual(0, system.ApplyGameplayEffect(effect, Context()));

            Assert.AreEqual(1, Collect(AbilityEventKind.EffectBlocked).Count);
            Assert.AreEqual(1, Collect(AbilityEventKind.EffectRefused).Count);
            Assert.IsEmpty(Collect(AbilityEventKind.EffectApplied));
            Assert.IsEmpty(Collect(AbilityEventKind.EffectDelivered));
        }

        [Test]
        public void RemovedEffect_IsRecorded()
        {
            AddHealth(owner);
            GameplayEffectContainer container = GameplayEffectContainer.For(owner);
            GameplayEffectApplicationResult result = container.ApplyDynamic(
                GameplayEffectDurationPolicy.Duration, Health, AttributeOperation.Add, -10f, 5f);
            Assert.IsTrue(result.Succeeded);

            Assert.IsTrue(container.TryRemove(result.Handle));

            Assert.AreEqual(AbilityEventKind.EffectRemoved, system.History.Latest.Kind);
        }

        // ------------------------------------------------------------- tasks

        [Test]
        public void Tasks_AreRecordedWhenTheyStartAndEnd()
        {
            WaitDelayTask task = system.RunActorTask(new WaitDelayTask(10f));
            Assert.IsTrue(task.IsRunning);
            Assert.AreEqual(AbilityEventKind.TaskStarted, system.History.Latest.Kind);
            StringAssert.Contains(nameof(WaitDelayTask), system.History.Latest.Message);

            system.ActorTasks.CancelAll();

            Assert.AreEqual(AbilityEventKind.TaskEnded, system.History.Latest.Kind);
            StringAssert.Contains("cancelled", system.History.Latest.Message);
        }

        // ------------------------------------------------------- the switch

        [Test]
        public void Disabled_RecordsNothing_AndCreatesNoHistory()
        {
            AbilityDiagnostics.Enabled = false;
            AbilityDefinition ability = Grant(NewAbility("test.quiet"));

            Assert.IsFalse(system.TryActivate(NewAbility("test.stranger"), Context()));
            Assert.IsTrue(system.TryActivate(ability, Context()));
            system.ForceCancelActiveAbility(CommonGameplayTags.CancelManual);
            system.TriggerGameplayCue(new GameplayTag("Cue.Quiet"), Context());

            Assert.IsFalse(system.HasHistory);
        }

        [Test]
        public void Disabled_RaisesNoGlobalEvent()
        {
            AbilityDiagnostics.Enabled = false;
            int raised = 0;
            System.Action<AbilitySystem, AbilityEvent> handler = (_, _) => raised++;
            AbilityDiagnostics.EventRecorded += handler;
            try
            {
                system.TryActivate(NewAbility("test.stranger"), Context());
            }
            finally
            {
                AbilityDiagnostics.EventRecorded -= handler;
            }

            Assert.AreEqual(0, raised);
        }

        [Test]
        public void Recorded_ReachesTheGlobalListener_WithTheActor()
        {
            AbilitySystem seen = null;
            AbilityEvent seenEvent = default;
            System.Action<AbilitySystem, AbilityEvent> handler = (actor, recorded) =>
            {
                seen = actor;
                seenEvent = recorded;
            };
            AbilityDiagnostics.EventRecorded += handler;
            try
            {
                system.TryActivate(NewAbility("test.stranger"), Context());
            }
            finally
            {
                AbilityDiagnostics.EventRecorded -= handler;
            }

            Assert.AreSame(system, seen);
            Assert.AreEqual(AbilityEventKind.ActivationRejected, seenEvent.Kind);
        }

        [Test]
        public void Entries_CarryTheFrameTheyHappenedOn()
        {
            system.TryActivate(NewAbility("test.stranger"), Context());

            Assert.AreEqual(Time.frameCount, system.History.Latest.Frame);
            Assert.GreaterOrEqual(system.History.Latest.RealTime, 0f);
        }

        [Test]
        public void HistoryCapacity_IsTakenWhenTheHistoryIsCreated()
        {
            AbilityDiagnostics.HistoryCapacity = 4;
            AbilityDefinition stranger = NewAbility("test.stranger");

            for (int i = 0; i < 6; i++)
            {
                system.TryActivate(stranger, Context());
            }

            Assert.AreEqual(4, system.History.Capacity);
            Assert.AreEqual(4, system.History.Count);
            Assert.AreEqual(2, system.History.DroppedCount);
        }

        // ----------------------------------------------------------- helpers

        private static AbilityEvent Rejected(AbilityDefinition ability, string message)
        {
            return AbilityEvent.Rejected(
                ability,
                AbilityActivationResult.Rejected(AbilityActivationRejection.NotGranted, message));
        }

        private List<AbilityEvent> Collect(AbilityEventKind kind)
        {
            List<AbilityEvent> matching = new();
            if (!system.HasHistory)
            {
                return matching;
            }

            AbilityEventHistory history = system.History;
            for (int i = 0; i < history.Count; i++)
            {
                if (history[i].Kind == kind)
                {
                    matching.Add(history[i]);
                }
            }

            return matching;
        }

        private AbilityContext Context()
        {
            return AbilityContext.FromTarget(owner, null);
        }

        private void AddHealth(GameObject actor)
        {
            actor.AddComponent<AttributeSet>().SetInitialValues(new[]
            {
                new AttributeSet.InitialValue(Health, 100f, 0f, 100f),
            });
        }

        private GameplayEffectDefinition NewInstantEffect(
            GameplayEffectTargeting targeting, float healthDelta)
        {
            GameplayEffectDefinition effect =
                Own(ScriptableObject.CreateInstance<GameplayEffectDefinition>());
            effect.name = "GE_Test_History";
            effect.ConfigureForTests(
                GameplayEffectDurationPolicy.Instant,
                targeting,
                new[] { new GameplayEffectModifier(Health, AttributeOperation.Add, healthDelta) });
            return effect;
        }

        private AbilityDefinition NewAbility(string id)
        {
            AbilityDefinition ability = Own(ScriptableObject.CreateInstance<AbilityDefinition>());
            SetField(ability, "abilityId", id);
            SetField(ability, "requiresTarget", false);
            return ability;
        }

        private TimelineAbilityDefinition NewCombo(string id, int stepCount)
        {
            TimelineAbilityDefinition ability =
                Own(ScriptableObject.CreateInstance<TimelineAbilityDefinition>());
            SetField(ability, "abilityId", id);
            SetField(ability, "requiresTarget", false);
            var steps = new AbilityStep[stepCount];
            for (int i = 0; i < stepCount; i++)
            {
                steps[i] = new AbilityStep();
                steps[i].ConfigureForTests(1, 2, 10, 60f);
            }

            ability.SetStepsForTests(steps);
            return ability;
        }

        private T Grant<T>(T ability) where T : AbilityDefinition
        {
            AbilityLoadout loadout = Own(ScriptableObject.CreateInstance<AbilityLoadout>());
            SetField(loadout, "abilities", new AbilityDefinition[] { ability });
            SetField(system, "loadout", loadout);
            return ability;
        }

        private T Own<T>(T item) where T : Object
        {
            owned.Add(item);
            return item;
        }

        private static void SetField<TValue>(object target, string fieldName, TValue value)
        {
            for (System.Type type = target.GetType(); type != null; type = type.BaseType)
            {
                System.Reflection.FieldInfo field = type.GetField(
                    fieldName,
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    field.SetValue(target, value);
                    return;
                }
            }

            Assert.Fail($"No field named {fieldName}.");
        }
    }
}
