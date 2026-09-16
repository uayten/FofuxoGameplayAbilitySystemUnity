using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// One swing of an ability: its clip, its frame windows, and what it fires.
    /// An ability holds an ordered list of these, so a four-hit combo is one
    /// asset with four steps instead of four assets plus a sequence asset
    /// tying them together.
    ///
    /// What belongs here is what changes per swing. Cost, cooldown, range,
    /// activation tags and AI weight stay on the ability, because they are
    /// decided once when the ability starts, not again on every hit.
    /// </summary>
    [Serializable]
    public sealed class AbilityStep
    {
        [Header("Animation")]
        [Tooltip("Clip played by gameplay. Its frame rate and length drive the whole timeline: every frame field below is read against this clip.")]
        [SerializeField] private AnimationClip animationClip;
        [Tooltip("Seconds of crossfade from the previous state into this clip. Zero cuts straight to it.")]
        [SerializeField, Min(0f)] private float animationBlendDuration = 0.08f;
        [Header("Timeline (1-based frames)")]
        [Tooltip("Last frame of the wind-up. The Active phase starts on the next frame. Phases are informational: they do not by themselves apply damage.")]
        [SerializeField, Min(0)] private int startupEndFrame = 1;
        [Tooltip("Last frame of the Active phase; Recovery starts on the next frame. Damage is NOT applied here — add an Effect Trigger and set its Frame.")]
        [SerializeField, Min(1)] private int activeEndFrame = 2;
        [Tooltip("Minimum final frame for the step. When an Animation Clip is assigned, the resolved timeline extends through the clip's complete duration.")]
        [SerializeField, Min(1)] private int recoveryEndFrame = 60;
        [Tooltip("Frame rate used to convert frames into seconds when no Animation Clip is assigned. The clip's own frame rate wins whenever there is one.")]
        [SerializeField, Min(1f)] private float fallbackFrameRate = 60f;

        [Header("Action Windows (1-based frames)")]
        [Tooltip("First frame where owner movement is unlocked. Zero keeps movement locked until the step completes.")]
        [SerializeField, Min(0)] private int movementUnlockFrame;
        [Tooltip("First frame where a buffered input starts the next step. Zero waits for the step to complete.")]
        [SerializeField, Min(0)] private int comboContinuationFrame;
        [Tooltip("Last inclusive frame that accepts an input for the next step. Zero keeps the window open until the step completes.")]
        [SerializeField, Min(0)] private int comboInputEndFrame;
        [Tooltip("Extra frames past Combo Input End Frame that still accept an input. A press landing here advances at once instead of being dropped. Zero is no grace. Ignored while Combo Input End Frame is zero, because that window never closes early.")]
        [SerializeField, Min(0)] private int comboInputGraceFrames;

        [Header("Branching")]
        [Tooltip("Where this step sends the activation when it advances. The first branch whose tag the owner holds wins; with none matching, the activation moves to the following step.")]
        [SerializeField] private AbilityStepBranch[] branches = { };

        [Header("Tag Windows (1-based frames)")]
        [Tooltip("Tags held for part of this step: i-frames, movement locks, super armour. Several windows sharing the same frames turn on and off together. Tags that describe the whole activation belong on the ability instead.")]
        [SerializeField] private AbilityStepTagWindow[] tagWindows = { };

        [Header("Target Assist")]
        [Tooltip("Re-acquires a target when this step starts: rotates the owner toward it and closes the gap during startup. Leave empty to keep the target the activation chose.")]
        [SerializeField] private TargetAssistDefinition targetAssist;

        [Header("Displacement")]
        [Tooltip("Where the travel direction is read at activation. Context uses the activation context direction (rolls and dashes).")]
        [SerializeField] private AbilityDisplacementDirection displacementDirection;
        [Tooltip("Meters travelled over the displacement window. Zero disables displacement.")]
        [SerializeField, Min(0f)] private float displacementDistance;
        [Tooltip("First 1-based frame that moves the owner.")]
        [SerializeField, Min(1)] private int displacementStartFrame = 1;
        [Tooltip("Last 1-based frame that moves the owner.")]
        [SerializeField, Min(1)] private int displacementEndFrame = 1;

        [Header("Effects")]
        [Tooltip("Gameplay effects fired by this step, each at its own frame. This is where damage lands: one trigger, its Frame set to the contact frame.")]
        [SerializeField] private AbilityEffectTrigger[] effectTriggers = { };

        [Header("Gameplay Cues")]
        [Tooltip("Cosmetic cue tags fired by this step (VFX, SFX, camera shake). Cues never change gameplay state.")]
        [SerializeField] private GameplayCueTrigger[] cueTriggers = { };

        public AnimationClip AnimationClip => animationClip;
        public float AnimationBlendDuration => Mathf.Max(0f, animationBlendDuration);

        public int StartupEndFrame => Mathf.Max(0, startupEndFrame);
        public int ActiveEndFrame => Mathf.Max(1, activeEndFrame);
        public float FrameRate => animationClip != null && animationClip.frameRate > Mathf.Epsilon
            ? animationClip.frameRate
            : Mathf.Max(1f, fallbackFrameRate);
        public int AnimationFrameCount => animationClip != null
            ? Mathf.Max(1, Mathf.RoundToInt(animationClip.length * FrameRate))
            : 0;
        public int RecoveryEndFrame =>
            Mathf.Max(Mathf.Max(1, recoveryEndFrame), AnimationFrameCount);
        public float Duration => RecoveryEndFrame / FrameRate;

        public IReadOnlyList<AbilityStepTagWindow> TagWindows =>
            tagWindows ?? Array.Empty<AbilityStepTagWindow>();
        public bool HasTagWindows => tagWindows != null && tagWindows.Length > 0;

        public int MovementUnlockFrame => Mathf.Max(0, movementUnlockFrame);
        public int ComboContinuationFrame => Mathf.Max(0, comboContinuationFrame);
        public int ComboInputEndFrame => Mathf.Max(0, comboInputEndFrame);
        public int ComboInputGraceFrames =>
            ComboInputEndFrame > 0 ? Mathf.Max(0, comboInputGraceFrames) : 0;
        /// <summary>
        /// Last frame that accepts an input for the next step, late grace
        /// included. Zero means the step accepts one until it completes.
        /// </summary>
        public int ComboInputDeadlineFrame =>
            ComboInputEndFrame > 0 ? ComboInputEndFrame + ComboInputGraceFrames : 0;
        public IReadOnlyList<AbilityStepBranch> Branches =>
            branches ?? Array.Empty<AbilityStepBranch>();
        public bool HasBranches => branches != null && branches.Length > 0;

        public TargetAssistDefinition TargetAssist => targetAssist;
        public bool ApproachesTarget =>
            targetAssist != null && targetAssist.ApproachTarget;

        /// <summary>
        /// True when this step moves the owner at all, by either route. What
        /// the movement needs — a motor on the actor — is checked by the
        /// system at activation, because it is a fact about the actor.
        /// </summary>
        public bool MovesOwner => HasDisplacement || ApproachesTarget;

        public bool HasDisplacement => displacementDistance > Mathf.Epsilon;
        public AbilityDisplacementDirection DisplacementDirection => displacementDirection;
        public float DisplacementDistance => Mathf.Max(0f, displacementDistance);
        public int DisplacementStartFrame => Mathf.Max(1, displacementStartFrame);
        public int DisplacementEndFrame =>
            Mathf.Max(DisplacementStartFrame, displacementEndFrame);
        public float DisplacementDurationSeconds => HasDisplacement
            ? AbilityDisplacement.WindowDurationSeconds(
                DisplacementStartFrame,
                DisplacementEndFrame,
                FrameRate)
            : 0f;

        public IReadOnlyList<AbilityEffectTrigger> EffectTriggers => effectTriggers;
        public IReadOnlyList<GameplayCueTrigger> CueTriggers => cueTriggers;

        public AbilityPhase GetPhase(int currentFrame)
        {
            if (currentFrame <= StartupEndFrame)
            {
                return AbilityPhase.Startup;
            }

            return currentFrame <= ActiveEndFrame
                ? AbilityPhase.Active
                : AbilityPhase.Recovery;
        }

        /// <summary>
        /// True while a buffered input would carry the ability into its next
        /// step. A zero end frame keeps the window open to the last frame.
        /// </summary>
        public bool IsComboWindowOpen(int currentFrame)
        {
            if (currentFrame < ComboContinuationFrame)
            {
                return false;
            }

            return ComboInputEndFrame <= 0 || currentFrame <= ComboInputEndFrame;
        }

        /// <summary>
        /// True while a request for the next step is still accepted, including
        /// the late grace frames past <see cref="ComboInputEndFrame"/>. A press
        /// accepted here but outside <see cref="IsComboWindowOpen"/> is a late
        /// one: it advances immediately rather than waiting for a window that
        /// has already closed.
        /// </summary>
        public bool AcceptsComboInput(int currentFrame)
        {
            return ComboInputDeadlineFrame <= 0 || currentFrame <= ComboInputDeadlineFrame;
        }

        /// <summary>
        /// True when the request arrived after the authored window and only the
        /// grace frames kept it alive.
        /// </summary>
        public bool IsLateComboInput(int currentFrame)
        {
            return ComboInputEndFrame > 0 &&
                   currentFrame > ComboInputEndFrame &&
                   currentFrame <= ComboInputDeadlineFrame;
        }

        public bool TryValidate(int stepNumber, out string error)
        {
            if (ActiveEndFrame <= StartupEndFrame)
            {
                error = $"Step {stepNumber}: active window must end after the startup window.";
                return false;
            }

            if (RecoveryEndFrame < ActiveEndFrame)
            {
                error = $"Step {stepNumber}: recovery must end at or after the active window.";
                return false;
            }

            for (int i = 0; i < effectTriggers.Length; i++)
            {
                AbilityEffectTrigger trigger = effectTriggers[i];
                if (trigger.Effect == null)
                {
                    error = $"Step {stepNumber}: effect trigger {i + 1} has no effect assigned.";
                    return false;
                }

                if (trigger.Frame > RecoveryEndFrame)
                {
                    error = $"Step {stepNumber}: effect trigger {i + 1} is outside the timeline.";
                    return false;
                }

                // Reached indirectly: a broken effect is silent from the
                // ability's side, so the ability that fires it reports it.
                if (!trigger.Effect.TryValidate(out string effectError))
                {
                    error =
                        $"Step {stepNumber}: effect trigger {i + 1} " +
                        $"('{trigger.Effect.name}') is invalid: {effectError}";
                    return false;
                }
            }

            for (int i = 0; i < cueTriggers.Length; i++)
            {
                if (cueTriggers[i].Cue.IsEmpty)
                {
                    error = $"Step {stepNumber}: gameplay cue trigger {i + 1} has no cue tag assigned.";
                    return false;
                }
            }

            if (targetAssist != null && !targetAssist.TryValidate(out error))
            {
                error = $"Step {stepNumber}: target assist is invalid: {error}";
                return false;
            }

            if (ApproachesTarget && HasDisplacement)
            {
                error =
                    $"Step {stepNumber}: target-assist approach and displacement " +
                    "cannot share one step.";
                return false;
            }

            if (HasDisplacement)
            {
                if (DisplacementEndFrame <= DisplacementStartFrame)
                {
                    error = $"Step {stepNumber}: displacement must end after it starts.";
                    return false;
                }

                if (DisplacementEndFrame > RecoveryEndFrame)
                {
                    error = $"Step {stepNumber}: displacement end frame is outside the timeline.";
                    return false;
                }
            }

            IReadOnlyList<AbilityStepTagWindow> windows = TagWindows;
            for (int i = 0; i < windows.Count; i++)
            {
                AbilityStepTagWindow window = windows[i];
                if (window.Tag.IsEmpty)
                {
                    error = $"Step {stepNumber}: tag window {i + 1} has no tag assigned.";
                    return false;
                }

                if (window.StartFrame > RecoveryEndFrame)
                {
                    error = $"Step {stepNumber}: tag window {i + 1} starts outside the timeline.";
                    return false;
                }

                if (!window.RunsToEndOfStep && window.EndFrame > RecoveryEndFrame)
                {
                    error = $"Step {stepNumber}: tag window {i + 1} ends outside the timeline.";
                    return false;
                }
            }

            if (ComboInputEndFrame > 0 && ComboInputEndFrame < ComboContinuationFrame)
            {
                error = $"Step {stepNumber}: combo input window ends before it opens.";
                return false;
            }

            IReadOnlyList<AbilityStepBranch> stepBranches = Branches;
            for (int i = 0; i < stepBranches.Count; i++)
            {
                if (!stepBranches[i].IsValid)
                {
                    error = $"Step {stepNumber}: branch {i + 1} has no tag assigned.";
                    return false;
                }
            }

            error = null;
            return true;
        }

        /// <summary>
        /// The step this one advances into, given the tags the owner holds right
        /// now. The first matching branch wins; without one the activation falls
        /// through to <paramref name="nextStepIndex"/>, which is what every step
        /// with no branch authored does.
        /// </summary>
        /// <param name="holdsTag">
        /// Reads the owner's tags. The system passes its own tag lookup, so a
        /// branch sees exactly what an activation check would see.
        /// </param>
        public int ResolveNextStepIndex(int nextStepIndex, Func<GameplayTag, bool> holdsTag)
        {
            if (branches == null || holdsTag == null)
            {
                return nextStepIndex;
            }

            for (int i = 0; i < branches.Length; i++)
            {
                AbilityStepBranch branch = branches[i];
                if (branch.IsValid && holdsTag(branch.RequiredTag))
                {
                    return branch.TargetStepIndex;
                }
            }

            return nextStepIndex;
        }

        public void Sanitize()
        {
            startupEndFrame = Mathf.Max(0, startupEndFrame);
            activeEndFrame = Mathf.Max(startupEndFrame + 1, activeEndFrame);
            recoveryEndFrame = Mathf.Max(activeEndFrame, recoveryEndFrame);
            fallbackFrameRate = Mathf.Max(1f, fallbackFrameRate);
            animationBlendDuration = Mathf.Max(0f, animationBlendDuration);
            movementUnlockFrame = Mathf.Max(0, movementUnlockFrame);
            comboContinuationFrame = Mathf.Max(0, comboContinuationFrame);
            comboInputEndFrame = Mathf.Max(0, comboInputEndFrame);
            comboInputGraceFrames = Mathf.Max(0, comboInputGraceFrames);
            displacementDistance = Mathf.Max(0f, displacementDistance);
            displacementStartFrame = Mathf.Max(1, displacementStartFrame);
            displacementEndFrame = Mathf.Max(displacementStartFrame, displacementEndFrame);
        }

        internal void ConfigureForTests(
            int startup, int active, int recovery, float frameRate)
        {
            startupEndFrame = startup;
            activeEndFrame = active;
            recoveryEndFrame = recovery;
            fallbackFrameRate = frameRate;
        }

        internal void SetAnimationClipForTests(AnimationClip clip)
        {
            animationClip = clip;
        }

        internal void SetEffectTriggersForTests(AbilityEffectTrigger[] triggers)
        {
            effectTriggers = triggers ?? Array.Empty<AbilityEffectTrigger>();
        }

        internal void SetTagWindowsForTests(params AbilityStepTagWindow[] windows)
        {
            tagWindows = windows ?? Array.Empty<AbilityStepTagWindow>();
        }

        internal void SetCueTriggersForTests(GameplayCueTrigger[] triggers)
        {
            cueTriggers = triggers ?? Array.Empty<GameplayCueTrigger>();
        }

        internal void SetTargetAssistForTests(TargetAssistDefinition assist)
        {
            targetAssist = assist;
        }

        internal void ConfigureDisplacementForTests(
            AbilityDisplacementDirection direction, float distance, int startFrame, int endFrame)
        {
            displacementDirection = direction;
            displacementDistance = distance;
            displacementStartFrame = startFrame;
            displacementEndFrame = endFrame;
        }

        internal void ConfigureActionWindowsForTests(
            int movementUnlock, int comboContinuation, int comboInputEnd)
        {
            movementUnlockFrame = movementUnlock;
            comboContinuationFrame = comboContinuation;
            comboInputEndFrame = comboInputEnd;
        }

        internal void SetComboInputGraceForTests(int graceFrames)
        {
            comboInputGraceFrames = graceFrames;
        }

        internal void SetBranchesForTests(params AbilityStepBranch[] stepBranches)
        {
            branches = stepBranches ?? Array.Empty<AbilityStepBranch>();
        }
    }
}
