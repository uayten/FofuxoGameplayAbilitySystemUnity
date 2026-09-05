using System.Collections.Generic;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    [CreateAssetMenu(fileName = "Ability", menuName = "Fofuxo/Abilities/Ability")]
    public sealed class AbilityDefinition : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string abilityId;

        [Header("Animation")]
        [SerializeField] private AnimationClip animationClip;
        [SerializeField] private string animatorStateName;
        [SerializeField, Min(0f)] private float animationBlendDuration = 0.08f;

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

        [Header("Costs and Charges")]
        [SerializeField] private AbilityCost[] costs = { };
        [Tooltip("Charges available before the restore timer refills them. Zero means unlimited.")]
        [SerializeField, Min(0)] private int maxCharges;
        [Tooltip("Seconds to restore one charge. Zero restores all charges at once after the cooldown elapses.")]
        [SerializeField, Min(0f)] private float chargeRestoreTime;
        [SerializeField] private AbilityCancelMask allowedCancellation = AbilityCancelMask.All;
        [SerializeField] private bool lockMovementDuringAbility = true;

        [Header("Gameplay Tags")]
        [SerializeField] private GameplayTag[] requiredTags = { };
        [SerializeField] private GameplayTag[] blockedTags = { };
        [SerializeField] private GameplayTag[] grantedTags = { };

        [Header("Effects")]
        [SerializeField] private AbilityEffectTrigger[] effectTriggers = { };

        [Header("Gameplay Cues")]
        [SerializeField] private GameplayCueTrigger[] cueTriggers = { };

        [Header("AI")]
        [SerializeField, Min(0f)] private float baseAiWeight = 1f;

        public string AbilityId => abilityId ?? string.Empty;
        public AnimationClip AnimationClip => animationClip;
        public string AnimatorStateName => animatorStateName ?? string.Empty;
        public float AnimationBlendDuration => Mathf.Max(0f, animationBlendDuration);
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
        public IReadOnlyList<AbilityCost> Costs => costs;
        public int MaxCharges => Mathf.Max(0, maxCharges);
        public float ChargeRestoreTime => Mathf.Max(0f, chargeRestoreTime);
        public bool HasLimitedCharges => MaxCharges > 0;
        public bool LockMovementDuringAbility => lockMovementDuringAbility;
        public IReadOnlyList<GameplayTag> RequiredTags => requiredTags;
        public IReadOnlyList<GameplayTag> BlockedTags => blockedTags;
        public IReadOnlyList<GameplayTag> GrantedTags => grantedTags;
        public IReadOnlyList<AbilityEffectTrigger> EffectTriggers => effectTriggers;
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

        public bool TryValidate(out string error)
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
            baseAiWeight = Mathf.Max(0f, baseAiWeight);
        }
    }
}
