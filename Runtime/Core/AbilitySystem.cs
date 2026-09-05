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
        private readonly List<GameplayTag> firedCues = new();
        private readonly Dictionary<AbilityDefinition, float> charges = new();
        private readonly Dictionary<AbilityDefinition, float> chargeRestoreTimers = new();

        private AbilityInstance activeInstance;
        private AbilitySequenceDefinition activeSequence;
        private AbilityContext activeSequenceContext;
        private int activeSequenceStep;
        private bool awaitingManualAdvance;
        private float manualAdvanceDeadline;

        public AbilityDefinition ActiveAbility => activeInstance?.Definition;
        public AbilitySequenceDefinition ActiveSequence => activeSequence;
        public int ActiveFrame => activeInstance?.CurrentFrame ?? 0;

        public IReadOnlyCollection<GameplayTag> ActiveTags
        {
            get
            {
                var tags = new List<GameplayTag>(looseTags);
                foreach (KeyValuePair<GameplayTag, int> entry in grantedTagCounts)
                {
                    if (entry.Value > 0 && !tags.Contains(entry.Key))
                    {
                        tags.Add(entry.Key);
                    }
                }

                return tags;
            }
        }
        public AbilityPhase? ActivePhase => activeInstance?.CurrentPhase;
        public bool IsActive => activeInstance != null;
        public AbilityLoadout Loadout => loadout;

        public event Action<AbilityDefinition> AbilityStarted;
        public event Action<AbilityDefinition, AbilityPhase> AbilityPhaseChanged;
        public event Action<AbilityDefinition> AbilityCompleted;
        public event Action<AbilityDefinition, AbilityCancelReason> AbilityCancelled;
        public event Action<AbilitySequenceDefinition> SequenceCompleted;
        public event Action<AbilitySequenceDefinition, AbilityCancelReason> SequenceCancelled;
        /// <summary>
        /// Fires when a manual sequence finishes a step and waits for
        /// <see cref="TryAdvanceSequence"/> before running the next one.
        /// </summary>
        public event Action<AbilitySequenceDefinition, int> SequenceAwaitingAdvance;
        /// <summary>
        /// Fires when an ability with effect triggers completes without
        /// registering any hit.
        /// </summary>
        public event Action<AbilityDefinition, AbilityContext> AbilityWhiffed;

        public IAbilityReplicationSink ReplicationSink { get; set; }
        /// <summary>
        /// Fires for cosmetic cues only. Game code presents them as VFX, SFX,
        /// or UI and must never change gameplay state in response.
        /// </summary>
        public event Action<AbilityDefinition, GameplayTag, AbilityContext> GameplayCueTriggered;

        private void Awake()
        {
            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>(true);
            }
        }

        private void Update()
        {
            TickChargeRestore(Time.deltaTime);

            if (activeInstance == null)
            {
                if (awaitingManualAdvance &&
                    activeSequence != null &&
                    activeSequence.ManualAdvanceWindow > 0f &&
                    Time.time > manualAdvanceDeadline)
                {
                    CancelSequenceOnly(AbilityCancelReason.Manual);
                }

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
            firedCues.Clear();
            try
            {
                completed = activeInstance.Tick(this, Time.deltaTime, out previousPhase, firedCues);
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

            for (int i = 0; i < firedCues.Count; i++)
            {
                GameplayCueTriggered?.Invoke(
                    activeInstance.Definition,
                    firedCues[i],
                    activeInstance.Context);
                ReplicationSink?.OnGameplayCue(firedCues[i], activeInstance.Context);
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
            else if (activeSequence != null)
            {
                CancelSequenceOnly(AbilityCancelReason.Manual);
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

        public bool CanActivateSequence(
            AbilitySequenceDefinition sequence,
            AbilityContext context,
            out string rejectionReason)
        {
            if (sequence == null)
            {
                rejectionReason = "Sequence is null.";
                return false;
            }

            if (activeInstance != null || activeSequence != null)
            {
                rejectionReason = "Another ability or sequence is active.";
                return false;
            }

            if (loadout == null || !loadout.Contains(sequence))
            {
                rejectionReason = "Sequence is not granted by the current loadout.";
                return false;
            }

            if (sequence.Steps.Count == 0)
            {
                rejectionReason = "Sequence has no steps.";
                return false;
            }

            if (IsOnCooldown(sequence))
            {
                rejectionReason = "Sequence is on cooldown.";
                return false;
            }

            return CanActivateInternal(sequence.Steps[0], context, true, out rejectionReason);
        }

        public bool TryActivateSequence(
            AbilitySequenceDefinition sequence,
            AbilityContext context)
        {
            if (!CanActivateSequence(sequence, context, out _))
            {
                return false;
            }

            AbilityDefinition firstStep = sequence.Steps[0];
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

        /// <summary>
        /// Advances a manual sequence waiting after a completed step. Each
        /// call runs one step, so player combos can require one input per hit.
        /// </summary>
        public bool TryAdvanceSequence()
        {
            if (activeSequence == null ||
                !awaitingManualAdvance ||
                activeInstance != null ||
                activeSequenceStep < 0 ||
                activeSequenceStep >= activeSequence.Steps.Count)
            {
                return false;
            }

            if (activeSequence.ManualAdvanceWindow > 0f &&
                Time.time > manualAdvanceDeadline)
            {
                CancelSequenceOnly(AbilityCancelReason.Manual);
                return false;
            }

            AbilityDefinition nextStep = activeSequence.Steps[activeSequenceStep];
            awaitingManualAdvance = false;
            if (CanActivateInternal(nextStep, activeSequenceContext, true, out _) &&
                ActivateInternal(nextStep, activeSequenceContext))
            {
                return true;
            }

            CancelSequenceOnly(AbilityCancelReason.Manual);
            return false;
        }

        /// <summary>
        /// Cancels the active ability or a sequence waiting for manual advance.
        /// </summary>
        public bool TryCancelSequence(AbilityCancelReason reason)
        {
            if (activeInstance != null)
            {
                return TryCancelActiveAbility(reason);
            }

            if (activeSequence != null)
            {
                CancelSequenceOnly(reason);
                return true;
            }

            return false;
        }

        public bool IsAwaitingSequenceAdvance =>
            activeSequence != null && awaitingManualAdvance && activeInstance == null;

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

        /// <summary>
        /// Fires a cosmetic cue outside the ability timeline, for example an
        /// AI tell or a successful parry. Empty tags are ignored.
        /// </summary>
        public void TriggerGameplayCue(GameplayTag cue, AbilityContext context)
        {
            if (cue.IsEmpty)
            {
                return;
            }

            GameplayCueTriggered?.Invoke(activeInstance?.Definition, cue, context);
            ReplicationSink?.OnGameplayCue(cue, context);
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

            if (!isSequenceStep && IsAwaitingSequenceAdvance)
            {
                rejectionReason = "A sequence is waiting for manual advance.";
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

            if (ability.HasLimitedCharges && GetCharges(ability) < 1f)
            {
                rejectionReason = "Ability has no charges left.";
                return false;
            }

            AttributeSet attributeSet =
                context.Owner != null ? context.Owner.GetComponent<AttributeSet>() : null;
            foreach (AbilityCost cost in ability.Costs)
            {
                if (attributeSet == null || attributeSet.GetCurrent(cost.Attribute) < cost.Amount)
                {
                    rejectionReason = $"Insufficient attribute for cost: {cost.Attribute}.";
                    return false;
                }
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
            PayCosts(ability, context);
            ConsumeCharge(ability);

            if (ability.CooldownStartPolicy == AbilityCooldownStartPolicy.OnActivation)
            {
                StartCooldown(ability);
            }

            PlayAbilityAnimation(ability);
            AbilityStarted?.Invoke(ability);
            AbilityPhaseChanged?.Invoke(ability, activeInstance.CurrentPhase);
            ReplicationSink?.OnAbilityActivated(ability, context);
            return true;
        }

        private void CompleteActiveAbility()
        {
            AbilityDefinition completedAbility = activeInstance.Definition;
            AbilityContext completedContext = activeInstance.Context;
            bool hitAnything = activeInstance.RegisteredHitCount > 0;
            bool tracksHits = completedAbility.EffectTriggers.Count > 0;
            RemoveGrantedTags(completedAbility);
            activeInstance = null;

            if (completedAbility.CooldownStartPolicy == AbilityCooldownStartPolicy.OnCompletion)
            {
                StartCooldown(completedAbility);
            }

            AbilityCompleted?.Invoke(completedAbility);
            ReplicationSink?.OnAbilityEnded(completedAbility, completedContext, true);
            if (tracksHits && !hitAnything)
            {
                AbilityWhiffed?.Invoke(completedAbility, completedContext);
            }

            if (activeSequence == null)
            {
                return;
            }

            activeSequenceStep++;
            if (activeSequenceStep < activeSequence.Steps.Count)
            {
                if (activeSequence.Advancement == SequenceAdvancement.Manual)
                {
                    awaitingManualAdvance = true;
                    manualAdvanceDeadline = activeSequence.ManualAdvanceWindow > 0f
                        ? Time.time + activeSequence.ManualAdvanceWindow
                        : float.PositiveInfinity;
                    SequenceAwaitingAdvance?.Invoke(activeSequence, activeSequenceStep);
                    return;
                }

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
            AbilityContext cancelledContext = activeInstance.Context;
            RemoveGrantedTags(cancelledAbility);
            activeInstance = null;
            AbilityCancelled?.Invoke(cancelledAbility, reason);
            ReplicationSink?.OnAbilityEnded(cancelledAbility, cancelledContext, false);

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
            awaitingManualAdvance = false;
            manualAdvanceDeadline = 0f;
        }

        private void StartCooldown(AbilityDefinition ability)
        {
            if (ability.Cooldown > 0f)
            {
                cooldownEndTimes[ability] = Time.time + ability.Cooldown;
            }
        }

        private float GetCharges(AbilityDefinition ability)
        {
            if (!ability.HasLimitedCharges)
            {
                return float.PositiveInfinity;
            }

            if (!charges.TryGetValue(ability, out float remaining))
            {
                remaining = ability.MaxCharges;
                charges[ability] = remaining;
            }

            return remaining;
        }

        private void ConsumeCharge(AbilityDefinition ability)
        {
            if (!ability.HasLimitedCharges)
            {
                return;
            }

            charges[ability] = Mathf.Max(0f, GetCharges(ability) - 1f);
            chargeRestoreTimers[ability] = 0f;
        }

        private void TickChargeRestore(float deltaTime)
        {
            if (charges.Count == 0)
            {
                return;
            }

            var drained = new List<AbilityDefinition>();
            foreach (KeyValuePair<AbilityDefinition, float> entry in charges)
            {
                AbilityDefinition ability = entry.Key;
                if (ability == null || entry.Value >= ability.MaxCharges)
                {
                    continue;
                }

                if (ability.ChargeRestoreTime > 0f)
                {
                    chargeRestoreTimers.TryGetValue(ability, out float elapsed);
                    elapsed += Mathf.Max(0f, deltaTime);
                    if (elapsed < ability.ChargeRestoreTime)
                    {
                        chargeRestoreTimers[ability] = elapsed;
                        continue;
                    }

                    chargeRestoreTimers[ability] = 0f;
                    charges[ability] = Mathf.Min(ability.MaxCharges, entry.Value + 1f);
                }
                else if (!IsOnCooldown(ability))
                {
                    charges[ability] = ability.MaxCharges;
                }

                if (charges[ability] >= ability.MaxCharges)
                {
                    drained.Add(ability);
                }
            }

            foreach (AbilityDefinition ability in drained)
            {
                charges.Remove(ability);
                chargeRestoreTimers.Remove(ability);
            }
        }

        private void PayCosts(AbilityDefinition ability, AbilityContext context)
        {
            if (ability.Costs.Count == 0 || context.Owner == null)
            {
                return;
            }

            AttributeSet attributeSet = context.Owner.GetComponent<AttributeSet>();
            if (attributeSet == null)
            {
                return;
            }

            foreach (AbilityCost cost in ability.Costs)
            {
                attributeSet.ApplyInstantModifier(new AttributeModifier(
                    cost.Attribute, AttributeOperation.Add, -cost.Amount, context.Owner));
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
