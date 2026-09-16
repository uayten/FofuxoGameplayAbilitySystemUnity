using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    /// <summary>
    /// The step policies added on top of Automatic and Manual: advancing on an
    /// event or on a condition, the late grace window, branches, the timeout
    /// policies, and the queued intent / deadline / transition reason a caller
    /// reads to find out what became of its press.
    /// </summary>
    public sealed class AbilityStepPolicyTests
    {
        private const float Tick = 1f / 60f;
        private static readonly GameplayTag ChainEvent = new("Event.Chain");
        private static readonly GameplayTag Charged = new("State.Charged");

        private readonly List<Object> owned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in owned)
            {
                if (o != null)
                {
                    Object.DestroyImmediate(o);
                }
            }

            owned.Clear();
        }

        // ------------------------------------------------- event advancement

        [Test]
        public void OnEvent_CarriesTheComboIntoItsNextStep()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition combo = NewCombo("test.step.event", 2);
            combo.SetStepAdvancementForTests(AbilityStepAdvancement.OnEvent);
            combo.SetStepAdvanceEventTagForTests(ChainEvent);
            combo.StepAt(0).ConfigureActionWindowsForTests(0, 2, 0);
            Grant(system, combo);

            Assert.IsTrue(system.TryActivate(combo, AbilityContext.FromTarget(owner, null)));
            system.Tick(Tick);

            Assert.IsTrue(system.TryAdvanceStepOnEvent(ChainEvent));
            system.Tick(Tick);

            Assert.AreEqual(1, system.ActiveStepIndex, "the event advanced the step");
            Assert.AreEqual(
                AbilityStepTransitionReason.QueuedIntent, system.LastTransition.Reason);
        }

        [Test]
        public void OnEvent_IgnoresAnEventItDoesNotDeclare()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition combo = NewCombo("test.step.event.other", 2);
            combo.SetStepAdvancementForTests(AbilityStepAdvancement.OnEvent);
            combo.SetStepAdvanceEventTagForTests(ChainEvent);
            Grant(system, combo);

            Assert.IsTrue(system.TryActivate(combo, AbilityContext.FromTarget(owner, null)));
            system.Tick(Tick);

            Assert.IsFalse(system.TryAdvanceStepOnEvent(new GameplayTag("Event.Unrelated")));
            system.Tick(Tick);

            Assert.AreEqual(0, system.ActiveStepIndex, "and the step never moved");
        }

        // --------------------------------------------- condition advancement

        [Test]
        public void OnCondition_AdvancesTheFrameTheConditionTurnsTrue()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition combo = NewCombo("test.step.condition", 2);
            combo.SetStepAdvancementForTests(AbilityStepAdvancement.OnCondition);
            combo.StepAt(0).ConfigureActionWindowsForTests(0, 2, 0);
            Grant(system, combo);

            bool ready = false;
            system.StepAdvanceCondition = _ => ready;

            Assert.IsTrue(system.TryActivate(combo, AbilityContext.FromTarget(owner, null)));
            system.Tick(Tick);
            system.Tick(Tick);
            Assert.AreEqual(0, system.ActiveStepIndex, "the condition is still false");

            ready = true;
            system.Tick(Tick);

            Assert.AreEqual(1, system.ActiveStepIndex, "the condition carried the combo on");
        }

        [Test]
        public void OnCondition_EndsOnItsCurrentStepWhenTheConditionNeverHolds()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition combo = NewCombo("test.step.condition.never", 2);
            combo.SetStepAdvancementForTests(AbilityStepAdvancement.OnCondition);
            Grant(system, combo);
            system.StepAdvanceCondition = _ => false;

            Assert.IsTrue(system.TryActivate(combo, AbilityContext.FromTarget(owner, null)));
            RunUntilIdle(system);

            Assert.IsFalse(system.IsActive);
            Assert.AreEqual(
                AbilityStepTransitionReason.TimeoutCompleted,
                system.LastTransition.Reason,
                "the timeout policy ended it, and said so");
        }

        // -------------------------------------------------- late grace window

        [Test]
        public void LateGraceWindow_AcceptsAPressPastTheInputEndFrame()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition combo = NewCombo("test.step.grace", 2);
            combo.SetStepAdvancementForTests(AbilityStepAdvancement.Manual);
            combo.StepAt(0).ConfigureActionWindowsForTests(0, 2, 3);
            combo.StepAt(0).SetComboInputGraceForTests(4);
            Grant(system, combo);

            Assert.IsTrue(system.TryActivate(combo, AbilityContext.FromTarget(owner, null)));
            for (int i = 0; i < 4; i++)
            {
                system.Tick(Tick);
            }

            Assert.Greater(
                system.ActiveFrame,
                combo.StepAt(0).ComboInputEndFrame,
                "the authored window has closed");
            Assert.IsTrue(system.TryQueueStepAdvance(), "grace still accepts the press");

            Assert.AreEqual(1, system.ActiveStepIndex, "a late press advances at once");
            Assert.AreEqual(
                AbilityStepTransitionReason.LateGraceIntent, system.LastTransition.Reason);
        }

        [Test]
        public void WithoutGrace_ALatePressIsRefusedWithAnObservableReason()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition combo = NewCombo("test.step.nograce", 2);
            combo.SetStepAdvancementForTests(AbilityStepAdvancement.Manual);
            combo.StepAt(0).ConfigureActionWindowsForTests(0, 2, 3);
            Grant(system, combo);

            Assert.IsTrue(system.TryActivate(combo, AbilityContext.FromTarget(owner, null)));
            for (int i = 0; i < 4; i++)
            {
                system.Tick(Tick);
            }

            Assert.IsFalse(system.TryQueueStepAdvance());
            Assert.AreEqual(0, system.ActiveStepIndex, "and the step never moved");
            Assert.AreEqual(
                AbilityStepIntentStatus.Rejected,
                system.QueuedStepIntent.Status,
                "the press is accounted for, not silently dropped");
        }

        [Test]
        public void TheDeadlineFrameCountsTheGraceFrames()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition combo = NewCombo("test.step.deadline", 2);
            combo.SetStepAdvancementForTests(AbilityStepAdvancement.Manual);
            combo.StepAt(0).ConfigureActionWindowsForTests(0, 2, 3);
            combo.StepAt(0).SetComboInputGraceForTests(4);
            Grant(system, combo);

            Assert.IsTrue(system.TryActivate(combo, AbilityContext.FromTarget(owner, null)));

            Assert.AreEqual(7, system.ComboInputDeadlineFrame);
            Assert.Greater(system.ComboInputTimeRemaining, 0f);
            Assert.Greater(system.StepTimeRemaining, 0f);
        }

        // ------------------------------------------------------------ branches

        [Test]
        public void ABranchSendsTheComboToTheStepItsTagNames()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition combo = NewCombo("test.step.branch", 3);
            combo.SetStepAdvancementForTests(AbilityStepAdvancement.Automatic);
            combo.StepAt(0).SetBranchesForTests(new AbilityStepBranch(Charged, 2));
            Grant(system, combo);
            system.SetLooseTag(Charged, true);

            Assert.IsTrue(system.TryActivate(combo, AbilityContext.FromTarget(owner, null)));
            RunUntil(system, () => system.ActiveStepIndex != 0);

            Assert.AreEqual(2, system.ActiveStepIndex, "the branch skipped the middle step");
            Assert.AreEqual(AbilityStepTransitionReason.Branch, system.LastTransition.Reason);
        }

        [Test]
        public void WithoutItsTag_ABranchLeavesTheOrderAlone()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition combo = NewCombo("test.step.branch.inert", 3);
            combo.SetStepAdvancementForTests(AbilityStepAdvancement.Automatic);
            combo.StepAt(0).SetBranchesForTests(new AbilityStepBranch(Charged, 2));
            Grant(system, combo);

            Assert.IsTrue(system.TryActivate(combo, AbilityContext.FromTarget(owner, null)));
            RunUntil(system, () => system.ActiveStepIndex != 0);

            Assert.AreEqual(1, system.ActiveStepIndex, "the next step, as authored");
            Assert.AreEqual(
                AbilityStepTransitionReason.AutomaticChain, system.LastTransition.Reason);
        }

        [Test]
        public void ABranchOutsideTheStepListFailsValidation()
        {
            TimelineAbilityDefinition combo = NewCombo("test.step.branch.invalid", 2);
            combo.StepAt(0).SetBranchesForTests(new AbilityStepBranch(Charged, 7));

            Assert.IsFalse(combo.TryValidate(out string error));
            StringAssert.Contains("branch 1", error);
        }

        // ---------------------------------------------------- timeout policies

        [Test]
        public void TimeoutAdvance_ChainsWithoutAnyInput()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition combo = NewCombo("test.step.timeout.advance", 2);
            combo.SetStepAdvancementForTests(AbilityStepAdvancement.Manual);
            combo.SetStepTimeoutPolicyForTests(AbilityStepTimeoutPolicy.AdvanceStep);
            Grant(system, combo);

            Assert.IsTrue(system.TryActivate(combo, AbilityContext.FromTarget(owner, null)));
            RunUntil(system, () => system.ActiveStepIndex != 0);

            Assert.AreEqual(1, system.ActiveStepIndex);
            Assert.AreEqual(
                AbilityStepTransitionReason.TimeoutAdvanced, system.LastTransition.Reason);
        }

        [Test]
        public void TimeoutCancel_EndsTheAbilityAsACancellation()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition combo = NewCombo("test.step.timeout.cancel", 2);
            combo.SetStepAdvancementForTests(AbilityStepAdvancement.Manual);
            combo.SetStepTimeoutPolicyForTests(AbilityStepTimeoutPolicy.CancelAbility);
            Grant(system, combo);

            GameplayTag cancelledWith = default;
            system.AbilityCancelled += (_, tag) => cancelledWith = tag;

            Assert.IsTrue(system.TryActivate(combo, AbilityContext.FromTarget(owner, null)));
            RunUntilIdle(system);

            Assert.IsFalse(system.IsActive);
            Assert.AreEqual(CommonGameplayTags.CancelStepTimeout, cancelledWith);
            Assert.AreEqual(
                AbilityStepTransitionReason.TimeoutCancelled, system.LastTransition.Reason);
        }

        [Test]
        public void TimeoutHold_KeepsTheAbilityWaitingForAnAdvance()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition combo = NewCombo("test.step.timeout.hold", 2);
            combo.SetStepAdvancementForTests(AbilityStepAdvancement.Manual);
            combo.SetStepTimeoutPolicyForTests(AbilityStepTimeoutPolicy.HoldLastStep);
            combo.StepAt(0).ConfigureActionWindowsForTests(0, 2, 0);
            Grant(system, combo);

            Assert.IsTrue(system.TryActivate(combo, AbilityContext.FromTarget(owner, null)));
            for (int i = 0; i < 40; i++)
            {
                system.Tick(Tick);
            }

            Assert.IsTrue(system.IsActive, "holding instead of ending");
            Assert.IsTrue(system.IsHoldingLastStep);
            Assert.AreEqual(0, system.ActiveStepIndex);

            Assert.IsTrue(system.TryQueueStepAdvance());
            system.Tick(Tick);

            Assert.AreEqual(1, system.ActiveStepIndex, "the held step advanced when asked");
        }

        // ---------------------------------------------------- intent lifecycle

        [Test]
        public void ABufferedRequestIsConsumedExactlyOnce()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition combo = NewCombo("test.step.intent.once", 3);
            combo.SetStepAdvancementForTests(AbilityStepAdvancement.Manual);
            combo.StepAt(0).ConfigureActionWindowsForTests(0, 3, 10);
            Grant(system, combo);

            var statuses = new List<AbilityStepIntentStatus>();
            system.StepIntentChanged += (_, intent) => statuses.Add(intent.Status);

            Assert.IsTrue(system.TryActivate(combo, AbilityContext.FromTarget(owner, null)));
            Assert.IsTrue(system.TryQueueStepAdvance());
            Assert.IsTrue(system.HasQueuedStepAdvance, "queued, waiting for the window");

            RunUntil(system, () => system.ActiveStepIndex != 0);

            Assert.AreEqual(1, system.ActiveStepIndex, "one press, one step");
            Assert.AreEqual(1, statuses.FindAll(s => s == AbilityStepIntentStatus.Queued).Count);
            Assert.AreEqual(1, statuses.FindAll(s => s == AbilityStepIntentStatus.Consumed).Count);
            Assert.IsFalse(system.HasQueuedStepAdvance, "and nothing is left pending");
        }

        [Test]
        public void ARequestTheAbilityCannotUseExpiresWithAReason()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition combo = NewCombo("test.step.intent.expire", 2);
            combo.SetStepAdvancementForTests(AbilityStepAdvancement.Manual);
            combo.StepAt(0).ConfigureActionWindowsForTests(0, 8, 10);
            Grant(system, combo);

            var statuses = new List<AbilityStepIntentStatus>();
            system.StepIntentChanged += (_, intent) => statuses.Add(intent.Status);

            Assert.IsTrue(system.TryActivate(combo, AbilityContext.FromTarget(owner, null)));
            Assert.IsTrue(system.TryQueueStepAdvance());
            system.ForceCancelActiveAbility(CommonGameplayTags.CancelManual);

            Assert.Contains(AbilityStepIntentStatus.Expired, statuses);
            Assert.IsFalse(system.IsActive);
        }

        [Test]
        public void QueueingIsRefusedForAnAbilityThatChainsOnItsOwn()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition combo = NewCombo("test.step.intent.automatic", 2);
            combo.SetStepAdvancementForTests(AbilityStepAdvancement.Automatic);
            Grant(system, combo);

            Assert.IsTrue(system.TryActivate(combo, AbilityContext.FromTarget(owner, null)));

            Assert.IsFalse(
                system.TryQueueStepAdvance(),
                "an Automatic combo has nothing to buffer an input for");
        }

        // ---------------------------------------- cancellation propagation

        [Test]
        public void CancellingAStepLetsTheAbilityEndUnderItsTimeoutPolicy()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition combo = NewCombo("test.step.cancelstep", 2);
            combo.SetStepAdvancementForTests(AbilityStepAdvancement.Manual);
            Grant(system, combo);

            Assert.IsTrue(system.TryActivate(combo, AbilityContext.FromTarget(owner, null)));
            system.Tick(Tick);

            Assert.IsTrue(system.TryCancelActiveStep(CommonGameplayTags.CancelManual));

            Assert.IsFalse(system.IsActive, "the default policy ends the ability with the step");
            Assert.AreEqual(
                AbilityStepTransitionReason.TimeoutCompleted, system.LastTransition.Reason);
        }

        [Test]
        public void CancellingAStepOfAHoldingAbilityLeavesTheAbilityRunning()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition combo = NewCombo("test.step.cancelstep.hold", 2);
            combo.SetStepAdvancementForTests(AbilityStepAdvancement.Manual);
            combo.SetStepTimeoutPolicyForTests(AbilityStepTimeoutPolicy.HoldLastStep);
            Grant(system, combo);

            Assert.IsTrue(system.TryActivate(combo, AbilityContext.FromTarget(owner, null)));
            system.Tick(Tick);

            Assert.IsTrue(system.TryCancelActiveStep(CommonGameplayTags.CancelManual));

            Assert.IsTrue(system.IsActive, "the ability decides, not the step");
            Assert.IsTrue(system.IsHoldingLastStep);
        }

        [Test]
        public void AnUncancellableAbilityRefusesAStepCancel()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition combo = NewCombo("test.step.cancelstep.refused", 2);
            combo.SetCancellationForTests(AbilityCancelPolicy.Nothing);
            Grant(system, combo);

            Assert.IsTrue(system.TryActivate(combo, AbilityContext.FromTarget(owner, null)));
            system.Tick(Tick);

            Assert.IsFalse(system.TryCancelActiveStep(CommonGameplayTags.CancelManual));
            Assert.IsTrue(system.IsActive);
        }

        [Test]
        public void AStepWhoseTargetingPreludeFindsNothingEndsTheAbility()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            var target = new GameObject("PreludeTarget");
            owned.Add(target);
            target.transform.position = owner.transform.position + Vector3.forward;

            TimelineAbilityDefinition combo = NewCombo("test.step.prelude", 2);
            SetField(combo, "requiresTarget", true);
            combo.SetStepAdvancementForTests(AbilityStepAdvancement.Manual);
            combo.StepAt(0).ConfigureActionWindowsForTests(0, 2, 3);
            combo.StepAt(0).SetComboInputGraceForTests(4);
            // A query on a layer nothing occupies: it runs, and finds no one.
            combo.StepAt(1).SetTargetAssistForTests(NewMissingTargetAssist());
            Grant(system, combo);

            Assert.IsTrue(system.TryActivate(combo, AbilityContext.FromTarget(owner, target)));
            for (int i = 0; i < 4; i++)
            {
                system.Tick(Tick);
            }

            // The target is gone by the time the late press starts the next step,
            // so the step's prelude has nothing to commit to.
            Object.DestroyImmediate(target);
            system.TryQueueStepAdvance();

            Assert.IsFalse(
                system.IsActive,
                "the ability ended rather than running on with a step that never started");
            Assert.AreEqual(
                AbilityStepTransitionReason.TargetingPreludeFailed,
                system.LastTransition.Reason);
        }

        // ------------------------------------------------------------ helpers

        private static void RunUntilIdle(AbilitySystem system)
        {
            for (int i = 0; i < 240 && system.IsActive; i++)
            {
                system.Tick(Tick);
            }
        }

        private static void RunUntil(AbilitySystem system, System.Func<bool> done)
        {
            for (int i = 0; i < 240 && system.IsActive && !done(); i++)
            {
                system.Tick(Tick);
            }
        }

        private AbilitySystem NewSystem(out GameObject owner)
        {
            owner = new GameObject("AbilitySystemOwner");
            owned.Add(owner);
            return owner.AddComponent<AbilitySystem>();
        }

        private TimelineAbilityDefinition NewCombo(string abilityId, int stepCount)
        {
            TimelineAbilityDefinition ability = ScriptableObject.CreateInstance<TimelineAbilityDefinition>();
            owned.Add(ability);
            ability.SetAbilityIdForTests(abilityId);
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

        private TargetAssistDefinition NewMissingTargetAssist()
        {
            TargetAssistDefinition assist =
                ScriptableObject.CreateInstance<TargetAssistDefinition>();
            owned.Add(assist);
            LayerMask mask = default;
            // A layer the test scene puts nothing on: the query is live, and
            // returns empty, which is the case the prelude has to survive.
            mask.value = 1 << 31;
            SetField(assist, "targetLayers", mask);
            SetField(assist, "searchDistance", 5f);
            SetField(assist, "approachTarget", false);
            return assist;
        }

        private void Grant(AbilitySystem system, params AbilityDefinition[] abilities)
        {
            AbilityLoadout loadout = ScriptableObject.CreateInstance<AbilityLoadout>();
            owned.Add(loadout);
            SetField(loadout, "abilities", abilities);
            SetField(system, "loadout", loadout);
        }

        private static void SetField<TValue>(object target, string fieldName, TValue value)
        {
            // Walks up the hierarchy: a private field of a base class is
            // invisible to a single GetField call, and an ability's own fields
            // now sit one level above the timeline type most fixtures use.
            for (System.Type type = target.GetType(); type != null; type = type.BaseType)
            {
                FieldInfo field = type.GetField(
                    fieldName,
                    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
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
