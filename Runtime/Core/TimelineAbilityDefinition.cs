using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// An ability that runs an authored timeline: one or more ordered
    /// <see cref="AbilityStep"/>s, each with its clip, its frame windows, and
    /// what it fires on which frame. A four-hit combo is one asset with four
    /// steps, not four assets and a sequence tying them together.
    ///
    /// This is a *kind* of ability, not the shape every ability has. Everything
    /// decided once per activation — cost, cooldown, range, tags, charges,
    /// exclusion, cancellation — lives on <see cref="AbilityDefinition"/> and
    /// applies here unchanged. What this type adds is the timeline: a length,
    /// phases, frame-scheduled effects and cues, and a last frame to complete
    /// on. An ability whose animation is played elsewhere, or that has none,
    /// stays a plain <see cref="AbilityDefinition"/> and never carries an empty
    /// step list around.
    /// </summary>
    [CreateAssetMenu(
        fileName = "GA_NewTimelineAbility",
        menuName = "Fofuxo/Abilities/Timeline Ability")]
    public class TimelineAbilityDefinition : AbilityDefinition
    {
        [Header("Steps")]
        [Tooltip("Ordered swings. One step is a single action; several make a combo.")]
        [SerializeField] private AbilityStep[] steps = { new AbilityStep() };
        [Tooltip("Automatic chains straight into the next step. Manual waits for an input inside the step's combo window; On Event waits for the Step Advance Event tag; On Condition polls the ability's advance condition.")]
        [SerializeField] private AbilityStepAdvancement stepAdvancement;
        [Tooltip("Gameplay event that carries an On Event ability into its next step. Ignored by every other advancement mode.")]
        [SerializeField] private GameplayTag stepAdvanceEventTag;
        [Tooltip("What a waiting step does when its timeline runs out with nothing to advance it. Complete Ability is how a player combo ends on the swing it reached.")]
        [SerializeField] private AbilityStepTimeoutPolicy stepTimeoutPolicy;

        public IReadOnlyList<AbilityStep> Steps => steps;
        public int StepCount => steps == null ? 0 : steps.Length;
        public AbilityStepAdvancement StepAdvancement => stepAdvancement;
        public GameplayTag StepAdvanceEventTag => stepAdvanceEventTag;
        public AbilityStepTimeoutPolicy StepTimeoutPolicy => stepTimeoutPolicy;

        /// <summary>
        /// True for every mode but <see cref="AbilityStepAdvancement.Automatic"/>:
        /// the step needs something to happen before it chains, so its combo
        /// window and its timeout policy both matter.
        /// </summary>
        public bool WaitsToAdvance => stepAdvancement != AbilityStepAdvancement.Automatic;

        public bool IsCombo => StepCount > 1;

        /// <summary>The step an activation starts on. Never null for a valid ability.</summary>
        public AbilityStep FirstStep => StepAt(0);

        public AbilityStep StepAt(int index)
        {
            if (steps == null || steps.Length == 0)
            {
                return null;
            }

            return steps[Mathf.Clamp(index, 0, steps.Length - 1)];
        }

        /// <summary>Wall-clock length of every step back to back.</summary>
        public float TotalDuration
        {
            get
            {
                float total = 0f;
                for (int i = 0; i < StepCount; i++)
                {
                    AbilityStep step = steps[i];
                    if (step != null)
                    {
                        total += step.Duration;
                    }
                }

                return total;
            }
        }

        /// <summary>
        /// A step that displaces the owner, or approaches with its own assist,
        /// needs a motor just as much as an activation-time approach does.
        /// </summary>
        public override bool RequiresMotor
        {
            get
            {
                if (base.RequiresMotor)
                {
                    return true;
                }

                for (int i = 0; i < StepCount; i++)
                {
                    if (steps[i] != null && steps[i].MovesOwner)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// The running step decides when the owner gets movement back, which is
        /// what the unlock frame is for. Without a step the ability-level lock
        /// is the whole rule, exactly as it is for an ability with no timeline.
        /// </summary>
        public override bool IsMovementLockedDuring(AbilityInstance instance)
        {
            if (!LockMovementDuringAbility)
            {
                return false;
            }

            AbilityStep step = instance?.Step;
            if (step == null)
            {
                return true;
            }

            return step.MovementUnlockFrame == 0 ||
                   instance.CurrentFrame < step.MovementUnlockFrame;
        }

        /// <summary>
        /// Called for the first step too, right after activation. Only a
        /// timeline has steps to report, so only this type carries the hook.
        /// </summary>
        protected internal virtual void OnStepStarted(AbilityInstance instance, int stepIndex)
        {
        }

        /// <summary>
        /// The advance condition for <see cref="AbilityStepAdvancement.OnCondition"/>.
        /// Polled every tick while the running step's combo window is open, so
        /// it must be cheap and side-effect free — it is asked whether the step
        /// may advance, never told to advance it. Returning false leaves the
        /// current step under the step timeout policy. Assign
        /// <c>AbilitySystem.StepAdvanceCondition</c> instead when the condition
        /// belongs to the actor rather than to the ability type; a delegate
        /// assigned there takes precedence over this hook.
        /// </summary>
        protected internal virtual bool CanAdvanceStep(AbilityInstance instance)
        {
            return false;
        }

        public override bool TryValidate(out string error)
        {
            if (!base.TryValidate(out error))
            {
                return false;
            }

            for (int i = 0; i < StepCount; i++)
            {
                if (steps[i] == null)
                {
                    error = $"Step {i + 1} is empty.";
                    return false;
                }

                if (!steps[i].TryValidate(i + 1, out error))
                {
                    return false;
                }

                if (steps[i].MovementUnlockFrame > steps[i].RecoveryEndFrame)
                {
                    error = $"Step {i + 1}: movement unlock frame is outside the timeline.";
                    return false;
                }
            }

            if (stepAdvancement == AbilityStepAdvancement.OnEvent &&
                stepAdvanceEventTag.IsEmpty && IsCombo)
            {
                error =
                    "Advancement is On Event but no Step Advance Event tag is set, " +
                    "so nothing can carry the ability past its first step.";
                return false;
            }

            for (int i = 0; i < StepCount; i++)
            {
                AbilityStep step = steps[i];
                if (step == null)
                {
                    continue;
                }

                IReadOnlyList<AbilityStepBranch> stepBranches = step.Branches;
                for (int b = 0; b < stepBranches.Count; b++)
                {
                    if (stepBranches[b].TargetStepIndex >= StepCount)
                    {
                        error =
                            $"Step {i + 1}: branch {b + 1} targets step " +
                            $"{stepBranches[b].TargetStepIndex + 1}, and the ability " +
                            $"has {StepCount}.";
                        return false;
                    }
                }
            }

            if (TargetAssist != null && TargetAssist.ApproachTarget && FirstStep != null &&
                FirstStep.HasDisplacement)
            {
                error =
                    "An ability cannot combine target-assist approach with " +
                    "displacement on its first step.";
                return false;
            }

            error = null;
            return true;
        }

        public override bool TryGetAuthoringWarning(out string warning)
        {
            // A timeline with nothing on it is the one shape this type cannot
            // run: no step, no last frame, and no code of its own to end the
            // activation. The plain ability type is what that ability is.
            if (StepCount == 0)
            {
                warning =
                    "This timeline ability has no steps, so it has no timeline to " +
                    "run and nothing to end it. Add a step, or make it a plain " +
                    "Ability instead.";
                return true;
            }

            for (int i = 0; i < StepCount; i++)
            {
                AbilityStep step = steps[i];
                if (step == null || step.ComboContinuationFrame <= 0)
                {
                    continue;
                }

                for (int t = 0; t < step.EffectTriggers.Count; t++)
                {
                    if (step.EffectTriggers[t].Frame > step.ComboContinuationFrame)
                    {
                        warning =
                            $"Step {i + 1}: effect trigger {t + 1} fires at frame " +
                            $"{step.EffectTriggers[t].Frame}, after the combo continue frame " +
                            $"({step.ComboContinuationFrame}). It is skipped whenever the " +
                            "combo continues.";
                        return true;
                    }
                }
            }

            warning = null;
            return false;
        }

        protected override void OnValidate()
        {
            base.OnValidate();

            if (steps == null)
            {
                steps = Array.Empty<AbilityStep>();
            }

            for (int i = 0; i < steps.Length; i++)
            {
                steps[i]?.Sanitize();
            }
        }

        internal void SetStepsForTests(params AbilityStep[] newSteps)
        {
            steps = newSteps ?? Array.Empty<AbilityStep>();
        }

        /// <summary>
        /// The first step, created on demand. A fixture that configures frames
        /// says so by asking for the step rather than assuming one was handed
        /// to it.
        /// </summary>
        internal AbilityStep FirstStepForTests
        {
            get
            {
                if (StepCount == 0)
                {
                    steps = new[] { new AbilityStep() };
                }

                return steps[0];
            }
        }

        internal void SetStepAdvancementForTests(AbilityStepAdvancement advancement)
        {
            stepAdvancement = advancement;
        }

        internal void SetStepAdvanceEventTagForTests(GameplayTag tag)
        {
            stepAdvanceEventTag = tag;
        }

        internal void SetStepTimeoutPolicyForTests(AbilityStepTimeoutPolicy policy)
        {
            stepTimeoutPolicy = policy;
        }
    }
}
