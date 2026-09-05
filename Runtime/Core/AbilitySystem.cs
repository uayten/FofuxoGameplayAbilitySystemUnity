using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    [DisallowMultipleComponent]
    public sealed class AbilitySystem : MonoBehaviour
    {
        [SerializeField] private AbilityLoadout loadout;
        [SerializeField] private Animator animator;

        private readonly Dictionary<AbilityDefinition, float> cooldownEndTimes = new();
        private readonly Dictionary<AbilitySequenceDefinition, float> sequenceCooldownEndTimes = new();
        private readonly Dictionary<GameplayTag, int> grantedTagCounts = new();
        private readonly HashSet<GameplayTag> looseTags = new();

        private AbilityInstance activeInstance;
        private AbilitySequenceDefinition activeSequence;
        private AbilityContext activeSequenceContext;
        private int activeSequenceStep;

        public AbilityDefinition ActiveAbility => activeInstance?.Definition;
        public AbilitySequenceDefinition ActiveSequence => activeSequence;
        public AbilityPhase? ActivePhase => activeInstance?.CurrentPhase;
        public bool IsActive => activeInstance != null;
        public AbilityLoadout Loadout => loadout;

        public event Action<AbilityDefinition> AbilityStarted;
        public event Action<AbilityDefinition, AbilityPhase> AbilityPhaseChanged;
        public event Action<AbilityDefinition> AbilityCompleted;
        public event Action<AbilityDefinition, AbilityCancelReason> AbilityCancelled;
        public event Action<AbilitySequenceDefinition> SequenceCompleted;
        public event Action<AbilitySequenceDefinition, AbilityCancelReason> SequenceCancelled;

        private void Awake()
        {
            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>(true);
            }
        }

        private void Update()
        {
            if (activeInstance == null)
            {
                return;
            }

            if (activeInstance.Definition.RequiresTarget && activeInstance.Context.Target == null)
            {
                ForceCancelActiveAbility(AbilityCancelReason.TargetLost);
                return;
            }

            AbilityInstance instanceAtStart = activeInstance;
            bool completed;
            AbilityPhase previousPhase;
            try
            {
                completed = activeInstance.Tick(this, Time.deltaTime, out previousPhase);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                ForceCancelActiveAbility(AbilityCancelReason.Manual);
                return;
            }

            if (activeInstance != instanceAtStart)
            {
                return;
            }

            if (previousPhase != activeInstance.CurrentPhase)
            {
                AbilityPhaseChanged?.Invoke(activeInstance.Definition, activeInstance.CurrentPhase);
            }

            if (completed)
            {
                CompleteActiveAbility();
            }
        }

        private void OnDisable()
        {
            if (activeInstance != null)
            {
                CancelActiveAbilityInternal(AbilityCancelReason.Manual);
            }

            looseTags.Clear();
            grantedTagCounts.Clear();
        }

        public bool CanActivate(
            AbilityDefinition ability,
            AbilityContext context,
            out string rejectionReason)
        {
            return CanActivateInternal(ability, context, false, out rejectionReason);
        }

        public bool TryActivate(AbilityDefinition ability, AbilityContext context)
        {
            if (!CanActivateInternal(ability, context, false, out _))
            {
                return false;
            }

            return ActivateInternal(ability, context);
        }

        public bool TryActivateSequence(
            AbilitySequenceDefinition sequence,
            AbilityContext context)
        {
            if (sequence == null ||
                activeInstance != null ||
                activeSequence != null ||
                loadout == null ||
                !loadout.Contains(sequence) ||
                sequence.Steps.Count == 0 ||
                IsOnCooldown(sequence))
            {
                return false;
            }

            AbilityDefinition firstStep = sequence.Steps[0];
            if (!CanActivateInternal(firstStep, context, true, out _))
            {
                return false;
            }

            activeSequence = sequence;
            activeSequenceContext = context;
            activeSequenceStep = 0;
            if (ActivateInternal(firstStep, context))
            {
                return true;
            }

            ClearActiveSequence();
            return false;
        }

        public bool TryCancelActiveAbility(AbilityCancelReason reason)
        {
            if (activeInstance == null ||
                !activeInstance.Definition.CanBeCancelledBy(reason))
            {
                return false;
            }

            CancelActiveAbilityInternal(reason);
            return true;
        }

        public void ForceCancelActiveAbility(AbilityCancelReason reason)
        {
            if (activeInstance != null)
            {
                CancelActiveAbilityInternal(reason);
            }
        }

        public bool IsOnCooldown(AbilityDefinition ability)
        {
            return ability != null &&
                   cooldownEndTimes.TryGetValue(ability, out float endTime) &&
                   Time.time < endTime;
        }

        public bool IsOnCooldown(AbilitySequenceDefinition sequence)
        {
            return sequence != null &&
                   sequenceCooldownEndTimes.TryGetValue(sequence, out float endTime) &&
                   Time.time < endTime;
        }

        public bool HasTag(GameplayTag tag)
        {
            return !tag.IsEmpty &&
                   (looseTags.Contains(tag) ||
                    grantedTagCounts.TryGetValue(tag, out int count) && count > 0);
        }

        public void SetLooseTag(GameplayTag tag, bool enabled)
        {
            if (tag.IsEmpty)
            {
                return;
            }

            if (enabled)
            {
                looseTags.Add(tag);
            }
            else
            {
                looseTags.Remove(tag);
            }
        }

        public AbilityDefinition FindAbility(string abilityId)
        {
            return loadout != null ? loadout.FindAbility(abilityId) : null;
        }

        private bool CanActivateInternal(
            AbilityDefinition ability,
            AbilityContext context,
            bool isSequenceStep,
            out string rejectionReason)
        {
            if (ability == null)
            {
                rejectionReason = "Ability is null.";
                return false;
            }

            if (!ability.TryValidate(out rejectionReason))
            {
                return false;
            }

            if (activeInstance != null)
            {
                rejectionReason = "Another ability is active.";
                return false;
            }

            if (!isSequenceStep && (loadout == null || !loadout.Contains(ability)))
            {
                rejectionReason = "Ability is not granted by the current loadout.";
                return false;
            }

            if (IsOnCooldown(ability))
            {
                rejectionReason = "Ability is on cooldown.";
                return false;
            }

            foreach (GameplayTag requiredTag in ability.RequiredTags)
            {
                if (!HasTag(requiredTag))
                {
                    rejectionReason = $"Required tag is missing: {requiredTag}.";
                    return false;
                }
            }

            foreach (GameplayTag blockedTag in ability.BlockedTags)
            {
                if (HasTag(blockedTag))
                {
                    rejectionReason = $"Activation is blocked by tag: {blockedTag}.";
                    return false;
                }
            }

            if (ability.RequiresTarget && context.Target == null)
            {
                rejectionReason = "Ability requires a target.";
                return false;
            }

            if (context.Owner != null && context.Target != null)
            {
                Vector3 targetDirection = Vector3.ProjectOnPlane(
                    context.Target.transform.position - context.Owner.transform.position,
                    Vector3.up);
                float distance = targetDirection.magnitude;
                if (distance < ability.MinimumRange || distance > ability.MaximumRange)
                {
                    rejectionReason = "Target is outside the configured range.";
                    return false;
                }

                if (targetDirection.sqrMagnitude > Mathf.Epsilon)
                {
                    float angle = Vector3.Angle(
                        context.Owner.transform.forward,
                        targetDirection.normalized);
                    if (angle > ability.MaximumFacingAngle)
                    {
                        rejectionReason = "Target is outside the configured facing angle.";
                        return false;
                    }
                }
            }

            rejectionReason = string.Empty;
            return true;
        }

        private bool ActivateInternal(AbilityDefinition ability, AbilityContext context)
        {
            activeInstance = new AbilityInstance(ability, context);
            AddGrantedTags(ability);

            if (ability.CooldownStartPolicy == AbilityCooldownStartPolicy.OnActivation)
            {
                StartCooldown(ability);
            }

            PlayAbilityAnimation(ability);
            AbilityStarted?.Invoke(ability);
            AbilityPhaseChanged?.Invoke(ability, activeInstance.CurrentPhase);
            return true;
        }

        private void CompleteActiveAbility()
        {
            AbilityDefinition completedAbility = activeInstance.Definition;
            RemoveGrantedTags(completedAbility);
            activeInstance = null;

            if (completedAbility.CooldownStartPolicy == AbilityCooldownStartPolicy.OnCompletion)
            {
                StartCooldown(completedAbility);
            }

            AbilityCompleted?.Invoke(completedAbility);

            if (activeSequence == null)
            {
                return;
            }

            activeSequenceStep++;
            if (activeSequenceStep < activeSequence.Steps.Count)
            {
                AbilityDefinition nextStep = activeSequence.Steps[activeSequenceStep];
                if (CanActivateInternal(nextStep, activeSequenceContext, true, out _) &&
                    ActivateInternal(nextStep, activeSequenceContext))
                {
                    return;
                }

                CancelSequenceOnly(AbilityCancelReason.Manual);
                return;
            }

            AbilitySequenceDefinition completedSequence = activeSequence;
            if (completedSequence.Cooldown > 0f)
            {
                sequenceCooldownEndTimes[completedSequence] =
                    Time.time + completedSequence.Cooldown;
            }

            ClearActiveSequence();
            SequenceCompleted?.Invoke(completedSequence);
        }

        private void CancelActiveAbilityInternal(AbilityCancelReason reason)
        {
            AbilityDefinition cancelledAbility = activeInstance.Definition;
            RemoveGrantedTags(cancelledAbility);
            activeInstance = null;
            AbilityCancelled?.Invoke(cancelledAbility, reason);

            if (activeSequence != null)
            {
                CancelSequenceOnly(reason);
            }
        }

        private void CancelSequenceOnly(AbilityCancelReason reason)
        {
            AbilitySequenceDefinition cancelledSequence = activeSequence;
            ClearActiveSequence();
            SequenceCancelled?.Invoke(cancelledSequence, reason);
        }

        private void ClearActiveSequence()
        {
            activeSequence = null;
            activeSequenceContext = default;
            activeSequenceStep = 0;
        }

        private void StartCooldown(AbilityDefinition ability)
        {
            if (ability.Cooldown > 0f)
            {
                cooldownEndTimes[ability] = Time.time + ability.Cooldown;
            }
        }

        private void AddGrantedTags(AbilityDefinition ability)
        {
            foreach (GameplayTag tag in ability.GrantedTags)
            {
                if (tag.IsEmpty)
                {
                    continue;
                }

                grantedTagCounts.TryGetValue(tag, out int count);
                grantedTagCounts[tag] = count + 1;
            }
        }

        private void RemoveGrantedTags(AbilityDefinition ability)
        {
            foreach (GameplayTag tag in ability.GrantedTags)
            {
                if (!grantedTagCounts.TryGetValue(tag, out int count))
                {
                    continue;
                }

                if (count <= 1)
                {
                    grantedTagCounts.Remove(tag);
                }
                else
                {
                    grantedTagCounts[tag] = count - 1;
                }
            }
        }

        private void PlayAbilityAnimation(AbilityDefinition ability)
        {
            if (animator == null ||
                animator.runtimeAnimatorController == null ||
                string.IsNullOrWhiteSpace(ability.AnimatorStateName))
            {
                return;
            }

            string stateName = ability.AnimatorStateName.Contains(".")
                ? ability.AnimatorStateName
                : $"{animator.GetLayerName(0)}.{ability.AnimatorStateName}";
            animator.CrossFadeInFixedTime(
                stateName,
                ability.AnimationBlendDuration,
                0,
                0f);
        }
    }
}
