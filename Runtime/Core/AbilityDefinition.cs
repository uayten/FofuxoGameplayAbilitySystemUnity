using System.Collections.Generic;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    [CreateAssetMenu(fileName = "Ability", menuName = "Fofuxo/Abilities/Ability")]
    public class AbilityDefinition : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string abilityId;

        [Header("Animation")]
        [SerializeField] private AnimationClip animationClip;
        [SerializeField] private string animatorStateName;
        [SerializeField, Min(0f)] private float animationBlendDuration = 0.08f;
        [Tooltip("Preview-only animation. The Inspector preview shows ONLY this clip, never the gameplay clip. Empty means no preview. Editor-only: never used by gameplay or builds.")]
        [SerializeField] private AnimationClip previewAnimationClip;

        [Header("Targeting")]
        [SerializeField] private bool requiresTarget = true;
        [SerializeField, Min(0f)] private float minimumRange;
        [SerializeField, Min(0f)] private float maximumRange = 3f;
        [SerializeField, Range(0f, 180f)] private float maximumFacingAngle = 180f;

        [Header("Timeline (1-based frames)")]
        [SerializeField, Min(0)] private int startupEndFrame = 1;
        [SerializeField, Min(1)] private int activeEndFrame = 2;
        [SerializeField, Min(1)] private int recoveryEndFrame = 60;
        [SerializeField, Min(1f)] private float fallbackFrameRate = 60f;

        [Header("Activation")]
        [SerializeField, Min(0f)] private float cooldown;
        [SerializeField] private AbilityCooldownStartPolicy cooldownStartPolicy;

        [Header("Displacement")]
        [Tooltip("Where the travel direction is read at activation. Context uses the activation context direction (rolls and dashes).")]
        [SerializeField] private AbilityDisplacementDirection displacementDirection;
        [Tooltip("Meters travelled over the displacement window. Zero disables displacement.")]
        [SerializeField, Min(0f)] private float displacementDistance;
        [Tooltip("First 1-based timeline frame that moves the owner.")]
        [SerializeField, Min(1)] private int displacementStartFrame = 1;
        [Tooltip("Last 1-based timeline frame that moves the owner. Must be greater than the start frame.")]
        [SerializeField, Min(1)] private int displacementEndFrame = 1;

        [Header("Costs and Charges")]
        [SerializeField] private AbilityCost[] costs = { };
        [Tooltip("Charges available before the restore timer refills them. Zero means unlimited.")]
        [SerializeField, Min(0)] private int maxCharges;
        [Tooltip("Seconds to restore one charge. Zero restores all charges at once after the cooldown elapses.")]
        [SerializeField, Min(0f)] private float chargeRestoreTime;
        [SerializeField] private AbilityCancelMask allowedCancellation = AbilityCancelMask.All;
        [SerializeField] private bool lockMovementDuringAbility = true;

        [Header("Action Windows (1-based frames)")]
        [Tooltip("First frame where owner movement is unlocked. Zero keeps movement locked until the ability completes.")]
        [SerializeField, Min(0)] private int movementUnlockFrame;
        [Tooltip("First frame where a buffered manual-sequence input starts the next step. Zero waits for ability completion.")]
        [SerializeField, Min(0)] private int comboContinuationFrame;
        [Tooltip("Last inclusive frame that accepts a manual-sequence input. Zero uses the sequence's post-completion window.")]
        [SerializeField, Min(0)] private int comboInputEndFrame;

        [Header("Gameplay Tags")]
        [SerializeField] private GameplayTag[] requiredTags = { };
        [SerializeField] private GameplayTag[] blockedTags = { };
        [SerializeField] private GameplayTag[] grantedTags = { };

        [Header("Effects")]
        [SerializeField] private AbilityEffectTrigger[] effectTriggers = { };
        [Tooltip("Reactive rewards applied to the owner on a successful parry while this ability is active (e.g. the block heal). The numbers live here, not in components.")]
        [SerializeField] private AbilityEffectDefinition[] onParryEffects = { };
        [Tooltip("Nested ability resolved on activation before the parent animation (e.g. target assist inside attacks). Runs without its own cooldown, costs, tags, or animation; it may schedule startup approach movement on the parent.")]
        [SerializeField] private TargetAssistDefinition nestedAssist;

        [Header("Gameplay Cues")]
        [SerializeField] private GameplayCueTrigger[] cueTriggers = { };

        [Header("AI")]
        [SerializeField, Min(0f)] private float baseAiWeight = 1f;

        public string AbilityId => abilityId ?? string.Empty;
        public AnimationClip AnimationClip => animationClip;
        public string AnimatorStateName => animatorStateName ?? string.Empty;
        public float AnimationBlendDuration => Mathf.Max(0f, animationBlendDuration);
        /// <summary>
        /// Explicit preview-only clip. The Inspector preview shows only this
        /// clip, never the gameplay clip. Null (the default) means no
        /// preview. Editor-only: runtime playback always uses
        /// <see cref="AnimationClip"/>.
        /// </summary>
        public AnimationClip PreviewClip => previewAnimationClip;
        public bool HasAnimationPreview => PreviewClip != null;
        public bool RequiresTarget => requiresTarget;
        public float MinimumRange => Mathf.Max(0f, minimumRange);
        public float MaximumRange => Mathf.Max(MinimumRange, maximumRange);
        public float MaximumFacingAngle => Mathf.Clamp(maximumFacingAngle, 0f, 180f);
        public int StartupEndFrame => Mathf.Max(0, startupEndFrame);
        public int ActiveEndFrame => Mathf.Max(1, activeEndFrame);
        public int RecoveryEndFrame => Mathf.Max(1, recoveryEndFrame);
        public float FrameRate => animationClip != null && animationClip.frameRate > Mathf.Epsilon
            ? animationClip.frameRate
            : Mathf.Max(1f, fallbackFrameRate);
        public float Duration => RecoveryEndFrame / FrameRate;
        public float Cooldown => Mathf.Max(0f, cooldown);
        public AbilityCooldownStartPolicy CooldownStartPolicy => cooldownStartPolicy;
        public bool HasDisplacement => displacementDistance > Mathf.Epsilon;
        public AbilityDisplacementDirection DisplacementDirection => displacementDirection;
        public float DisplacementDistance => Mathf.Max(0f, displacementDistance);
        public int DisplacementStartFrame => Mathf.Max(1, displacementStartFrame);
        public int DisplacementEndFrame => Mathf.Max(1, displacementEndFrame);
        public float DisplacementDurationSeconds => HasDisplacement
            ? AbilityDisplacement.WindowDurationSeconds(
                DisplacementStartFrame,
                DisplacementEndFrame,
                FrameRate)
            : 0f;
        public IReadOnlyList<AbilityCost> Costs => costs;
        public int MaxCharges => Mathf.Max(0, maxCharges);
        public float ChargeRestoreTime => Mathf.Max(0f, chargeRestoreTime);
        public bool HasLimitedCharges => MaxCharges > 0;
        public bool LockMovementDuringAbility => lockMovementDuringAbility;
        public int MovementUnlockFrame => Mathf.Max(0, movementUnlockFrame);
        public int ComboContinuationFrame => Mathf.Max(0, comboContinuationFrame);
        public int ComboInputEndFrame => Mathf.Max(0, comboInputEndFrame);
        public IReadOnlyList<GameplayTag> RequiredTags => requiredTags;
        public IReadOnlyList<GameplayTag> BlockedTags => blockedTags;
        public IReadOnlyList<GameplayTag> GrantedTags => grantedTags;
        public IReadOnlyList<AbilityEffectTrigger> EffectTriggers => effectTriggers;
        public IReadOnlyList<AbilityEffectDefinition> OnParryEffects => onParryEffects;
        public TargetAssistDefinition NestedAssist => nestedAssist;
        public IReadOnlyList<GameplayCueTrigger> CueTriggers => cueTriggers;
        public float BaseAiWeight => Mathf.Max(0f, baseAiWeight);

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

        public bool CanBeCancelledBy(AbilityCancelReason reason)
        {
            AbilityCancelMask reasonMask = (AbilityCancelMask)(1 << (int)reason);
            return (allowedCancellation & reasonMask) != 0;
        }

        public bool IsMovementLockedAtFrame(int currentFrame)
        {
            return LockMovementDuringAbility &&
                   (MovementUnlockFrame == 0 || currentFrame < MovementUnlockFrame);
        }

        public virtual bool TryValidate(out string error)
        {
            if (string.IsNullOrWhiteSpace(AbilityId))
            {
                error = "Ability ID is required.";
                return false;
            }

            if (StartupEndFrame >= ActiveEndFrame)
            {
                error = "Active End Frame must be greater than Startup End Frame.";
                return false;
            }

            if (ActiveEndFrame > RecoveryEndFrame)
            {
                error = "Recovery End Frame must be greater than or equal to Active End Frame.";
                return false;
            }

            if (MovementUnlockFrame > RecoveryEndFrame)
            {
                error = "Movement Unlock Frame is outside the ability timeline.";
                return false;
            }

            if (ComboContinuationFrame > RecoveryEndFrame)
            {
                error = "Combo Continue Frame is outside the ability timeline.";
                return false;
            }

            if (ComboInputEndFrame > RecoveryEndFrame)
            {
                error = "Combo Input End Frame is outside the ability timeline.";
                return false;
            }

            if (ComboContinuationFrame > 0 &&
                ComboInputEndFrame > 0 &&
                ComboInputEndFrame < ComboContinuationFrame)
            {
                error = "Combo Input End Frame must be greater than or equal to Combo Continue Frame.";
                return false;
            }

            for (int i = 0; i < costs.Length; i++)
            {
                AbilityCost cost = costs[i];
                if (cost.Attribute.IsEmpty)
                {
                    error = $"Cost {i + 1} has no attribute assigned.";
                    return false;
                }

                if (cost.Amount <= 0f)
                {
                    error = $"Cost {i + 1} must be greater than zero.";
                    return false;
                }
            }

            if (HasLimitedCharges && ChargeRestoreTime <= 0f && Cooldown <= 0f)
            {
                error = "Limited charges require a charge restore time or a cooldown.";
                return false;
            }

            if (HasDisplacement)
            {
                if (DisplacementEndFrame <= DisplacementStartFrame)
                {
                    error = "Displacement end frame must be greater than the displacement start frame.";
                    return false;
                }

                if (DisplacementEndFrame > RecoveryEndFrame)
                {
                    error = "Displacement end frame is outside the ability timeline.";
                    return false;
                }
            }

            for (int i = 0; i < effectTriggers.Length; i++)
            {
                AbilityEffectTrigger trigger = effectTriggers[i];
                if (trigger.Effect == null)
                {
                    error = $"Effect trigger {i + 1} has no effect assigned.";
                    return false;
                }

                if (trigger.Frame > RecoveryEndFrame)
                {
                    error = $"Effect trigger {i + 1} is outside the ability timeline.";
                    return false;
                }

                if (ComboContinuationFrame > 0 && trigger.Frame > ComboContinuationFrame)
                {
                    error = $"Effect trigger {i + 1} occurs after the combo continuation frame.";
                    return false;
                }
            }

            for (int i = 0; i < onParryEffects.Length; i++)
            {
                if (onParryEffects[i] == null)
                {
                    error = $"Parry effect {i + 1} has no effect assigned.";
                    return false;
                }
            }

            if (nestedAssist != null && !nestedAssist.TryValidate(out error))
            {
                error = "Nested assist is invalid: " + error;
                return false;
            }

            if (nestedAssist != null && nestedAssist.ApproachTarget && HasDisplacement)
            {
                error = "An ability cannot combine target-assist approach with its own displacement.";
                return false;
            }

            for (int i = 0; i < cueTriggers.Length; i++)
            {
                GameplayCueTrigger cueTrigger = cueTriggers[i];
                if (cueTrigger.Cue.IsEmpty)
                {
                    error = $"Gameplay cue trigger {i + 1} has no cue tag assigned.";
                    return false;
                }

                if (cueTrigger.Frame > RecoveryEndFrame)
                {
                    error = $"Gameplay cue trigger {i + 1} is outside the ability timeline.";
                    return false;
                }
            }

            error = string.Empty;
            return true;
        }

        private void OnValidate()
        {
            abilityId = abilityId?.Trim();
            minimumRange = Mathf.Max(0f, minimumRange);
            maximumRange = Mathf.Max(minimumRange, maximumRange);
            startupEndFrame = Mathf.Max(0, startupEndFrame);
            activeEndFrame = Mathf.Max(startupEndFrame + 1, activeEndFrame);
            recoveryEndFrame = Mathf.Max(activeEndFrame, recoveryEndFrame);
            fallbackFrameRate = Mathf.Max(1f, fallbackFrameRate);
            cooldown = Mathf.Max(0f, cooldown);
            maxCharges = Mathf.Max(0, maxCharges);
            chargeRestoreTime = Mathf.Max(0f, chargeRestoreTime);
            movementUnlockFrame = Mathf.Max(0, movementUnlockFrame);
            comboContinuationFrame = Mathf.Max(0, comboContinuationFrame);
            comboInputEndFrame = Mathf.Max(0, comboInputEndFrame);
            baseAiWeight = Mathf.Max(0f, baseAiWeight);
            displacementDistance = Mathf.Max(0f, displacementDistance);
            displacementStartFrame = Mathf.Max(1, displacementStartFrame);
            displacementEndFrame = Mathf.Max(1, displacementEndFrame);
        }

        /// <summary>
        /// Test seam: authoring data stays immutable at runtime, but EditMode
        /// tests need invalid and valid displacement configurations that the
        /// Inspector clamps away.
        /// </summary>
        internal void SetNestedAssistForTests(TargetAssistDefinition assist)
        {
            nestedAssist = assist;
        }

        internal void SetParryEffectsForTests(params AbilityEffectDefinition[] effects)
        {
            onParryEffects = effects ?? System.Array.Empty<AbilityEffectDefinition>();
        }

        internal void SetAbilityIdForTests(string id)
        {
            abilityId = id?.Trim();
        }

        internal void SetAnimationClipsForTests(AnimationClip clip, AnimationClip previewClip)
        {
            animationClip = clip;
            previewAnimationClip = previewClip;
        }

        internal void ConfigureDisplacementForTests(
            AbilityDisplacementDirection direction,
            float distance,
            int startFrame,
            int endFrame)
        {
            displacementDirection = direction;
            displacementDistance = distance;
            displacementStartFrame = startFrame;
            displacementEndFrame = endFrame;
        }

        internal void ConfigureActionWindowsForTests(
            int unlockFrame,
            int continuationFrame,
            int inputEndFrame)
        {
            movementUnlockFrame = unlockFrame;
            comboContinuationFrame = continuationFrame;
            comboInputEndFrame = inputEndFrame;
        }
    }
}
