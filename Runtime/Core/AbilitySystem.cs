using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    [DisallowMultipleComponent]
    public sealed class AbilitySystem : MonoBehaviour
    {
        private const int AssistTargetCapacity = 32;
        private static readonly Collider[] AssistTargetBuffer = new Collider[AssistTargetCapacity];

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
        private bool queuedSequenceAdvance;
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
        public AbilityContext? ActiveContext => activeInstance?.Context;
        public bool HasActiveDisplacement => activeInstance?.HasActiveDisplacement ?? false;
        public bool IsMovementLocked =>
            activeInstance != null &&
            activeInstance.Definition.IsMovementLockedAtFrame(activeInstance.CurrentFrame);
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
            Tick(Time.deltaTime);
        }

        internal void Tick(float deltaTime)
        {
            TickChargeRestore(deltaTime);

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
                completed = activeInstance.Tick(this, deltaTime, out previousPhase, firedCues);
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

            ApplyActiveDisplacement(activeInstance, deltaTime);

            if (TryProcessQueuedSequenceAdvance())
            {
                return;
            }

            ExpireSequenceInputWindow();

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
        /// Records one input for the next step of a manual sequence. During an
        /// active step, the input is retained until its Combo Continue Frame;
        /// after the Combo Input End Frame it is rejected. Legacy steps whose
        /// frame window is zero continue to advance after normal completion.
        /// </summary>
        public bool TryQueueSequenceAdvance()
        {
            if (activeSequence == null ||
                activeSequence.Advancement != SequenceAdvancement.Manual ||
                activeSequenceStep >= activeSequence.Steps.Count - 1)
            {
                return false;
            }

            if (IsAwaitingSequenceAdvance)
            {
                return TryAdvanceSequence();
            }

            if (activeInstance == null)
            {
                return false;
            }

            int inputEndFrame = activeInstance.Definition.ComboInputEndFrame;
            if (inputEndFrame > 0 && ActiveFrame > inputEndFrame)
            {
                return false;
            }

            queuedSequenceAdvance = true;
            return true;
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

        /// <summary>
        /// Applies a reactive effect outside the timeline (parry rewards,
        /// cleanses). Runs with an ephemeral instance of the source ability,
        /// so hit-dedup and scaling resolve exactly like triggered effects.
        /// </summary>
        public void ApplyReactiveEffect(
            AbilityEffectDefinition effect,
            AbilityDefinition source,
            AbilityContext context)
        {
            if (effect == null || source == null)
            {
                return;
            }

            effect.Apply(new AbilityEffectContext(this, new AbilityInstance(source, context), -1));
        }

        /// <summary>
        /// Resolves the nested prelude of an activation: currently target
        /// assist (query enemies around the context direction, snap the owner,
        /// propagate the target, and calculate startup approach). Selection is
        /// instant and tagless; the parent keeps owning the timeline, movement,
        /// cooldown, and animation.
        /// </summary>
        private static AbilityContext ResolveNestedAssist(
            AbilityDefinition ability,
            AbilityContext context,
            out float approachDistance)
        {
            approachDistance = 0f;
            TargetAssistDefinition assist = ability.NestedAssist;
            GameObject owner = context.Owner;
            if (assist == null || owner == null)
            {
                return context;
            }

            int layers = assist.TargetLayerMask;
            if (layers == 0)
            {
                return context;
            }

            float distance = assist.ResolveSearchDistance();
            if (distance <= Mathf.Epsilon)
            {
                return context;
            }

            float cone = Mathf.Clamp(assist.ConeHalfAngle, 0f, 90f);
            Vector3 facing = context.Direction;
            if (facing.sqrMagnitude <= Mathf.Epsilon)
            {
                facing = owner.transform.forward;
            }

            int candidateCount = Physics.OverlapSphereNonAlloc(
                owner.transform.position,
                distance,
                AssistTargetBuffer,
                layers,
                QueryTriggerInteraction.Collide);

            float bestScore = float.PositiveInfinity;
            Vector3 bestDirection = Vector3.zero;
            float bestDistance = 0f;
            Component bestReceiver = null;
            for (int i = 0; i < candidateCount; i++)
            {
                Collider candidate = AssistTargetBuffer[i];
                if (candidate == null ||
                    candidate.gameObject == owner ||
                    candidate.transform.IsChildOf(owner.transform) ||
                    !candidate.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (!TryGetAssistDirection(owner, candidate, out Vector3 candidateDirection, out float candidateDistance))
                {
                    continue;
                }

                if (candidate.GetComponentInParent<IAbilityDamageReceiver>() is not IAbilityDamageReceiver receiver ||
                    receiver is not Component receiverComponent ||
                    !receiver.IsDamageable)
                {
                    continue;
                }

                float inputAngle = Vector3.Angle(facing, candidateDirection);
                if (candidateDistance > assist.ProximityRadius && inputAngle > cone)
                {
                    continue;
                }

                float score = candidateDistance + inputAngle * 0.02f;
                if (score >= bestScore)
                {
                    continue;
                }

                bestScore = score;
                bestDirection = candidateDirection;
                bestDistance = candidateDistance;
                bestReceiver = receiverComponent;
            }

            if (bestDirection.sqrMagnitude <= Mathf.Epsilon || bestReceiver == null)
            {
                return context;
            }

            owner.transform.rotation = Quaternion.LookRotation(bestDirection, Vector3.up);
            if (assist.ApproachTarget)
            {
                float stoppingDistance = assist.ResolveStoppingDistance();
                approachDistance = Mathf.Max(0f, bestDistance - stoppingDistance);
            }

            GameObject target = bestReceiver.gameObject;
            return new AbilityContext(
                owner,
                target,
                bestDirection,
                target.transform.position);
        }

        private static bool TryGetAssistDirection(
            GameObject owner,
            Collider targetCollider,
            out Vector3 targetDirection,
            out float targetDistance)
        {
            targetDirection = Vector3.zero;
            targetDistance = 0f;

            Vector3 closestPoint = targetCollider.ClosestPoint(owner.transform.position);
            Vector3 planarDirection = Vector3.ProjectOnPlane(
                closestPoint - owner.transform.position,
                Vector3.up);
            if (planarDirection.sqrMagnitude <= Mathf.Epsilon)
            {
                planarDirection = Vector3.ProjectOnPlane(
                    targetCollider.bounds.center - owner.transform.position,
                    Vector3.up);
            }

            if (planarDirection.sqrMagnitude <= Mathf.Epsilon)
            {
                return false;
            }

            targetDistance = planarDirection.magnitude;
            targetDirection = planarDirection / targetDistance;
            return true;
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
            AbilityContext resolvedContext = ResolveNestedAssist(
                ability,
                context,
                out float assistApproachDistance);
            activeInstance = new AbilityInstance(ability, resolvedContext);
            BeginInstanceDisplacement(
                activeInstance,
                ability,
                resolvedContext,
                assistApproachDistance);
            AddGrantedTags(ability);
            PayCosts(ability, resolvedContext);
            ConsumeCharge(ability);

            if (ability.CooldownStartPolicy == AbilityCooldownStartPolicy.OnActivation)
            {
                StartCooldown(ability);
            }

            PlayAbilityAnimation(ability);
            AbilityStarted?.Invoke(ability);
            AbilityPhaseChanged?.Invoke(ability, activeInstance.CurrentPhase);
            ReplicationSink?.OnAbilityActivated(ability, resolvedContext);
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
                    if (completedAbility.ComboInputEndFrame > 0 &&
                        !queuedSequenceAdvance)
                    {
                        CancelSequenceOnly(AbilityCancelReason.Manual);
                        return;
                    }

                    awaitingManualAdvance = true;
                    manualAdvanceDeadline = activeSequence.ManualAdvanceWindow > 0f
                        ? Time.time + activeSequence.ManualAdvanceWindow
                        : float.PositiveInfinity;
                    SequenceAwaitingAdvance?.Invoke(activeSequence, activeSequenceStep);
                    if (queuedSequenceAdvance)
                    {
                        queuedSequenceAdvance = false;
                        TryAdvanceSequence();
                    }
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
            queuedSequenceAdvance = false;
            manualAdvanceDeadline = 0f;
        }

        private bool TryProcessQueuedSequenceAdvance()
        {
            if (!queuedSequenceAdvance ||
                activeSequence == null ||
                activeInstance == null ||
                activeSequence.Advancement != SequenceAdvancement.Manual)
            {
                return false;
            }

            int continuationFrame = activeInstance.Definition.ComboContinuationFrame;
            if (continuationFrame == 0 || ActiveFrame < continuationFrame)
            {
                return false;
            }

            AbilityInstance completedInstance = activeInstance;
            CompleteActiveAbility();
            return activeInstance != completedInstance;
        }

        private void ExpireSequenceInputWindow()
        {
            if (queuedSequenceAdvance ||
                activeSequence == null ||
                activeInstance == null ||
                activeSequence.Advancement != SequenceAdvancement.Manual)
            {
                return;
            }

            int inputEndFrame = activeInstance.Definition.ComboInputEndFrame;
            if (inputEndFrame > 0 && ActiveFrame > inputEndFrame)
            {
                CancelSequenceOnly(AbilityCancelReason.Manual);
            }
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

        /// <summary>
        /// Resolves the travel snapshot for one activation. Direction follows
        /// the ability's displacement mode, the body is the owner's Rigidbody
        /// (null when the owner has none, which silently disables travel).
        /// </summary>
        private static void BeginInstanceDisplacement(
            AbilityInstance instance,
            AbilityDefinition ability,
            AbilityContext context,
            float assistApproachDistance)
        {
            if (instance == null)
            {
                return;
            }

            int startFrame;
            int endFrame;
            float distance;
            Vector3 direction;
            if (assistApproachDistance > Mathf.Epsilon)
            {
                startFrame = 1;
                endFrame = Mathf.Max(2, ability.StartupEndFrame);
                distance = assistApproachDistance;
                direction = context.Direction;
            }
            else if (ability.HasDisplacement)
            {
                startFrame = ability.DisplacementStartFrame;
                endFrame = ability.DisplacementEndFrame;
                distance = ability.DisplacementDistance;
                direction = AbilityDisplacement.ResolveDirection(
                    ability.DisplacementDirection,
                    context);
            }
            else
            {
                return;
            }

            Rigidbody body = context.Owner != null
                ? context.Owner.GetComponent<Rigidbody>()
                : null;
            float duration = AbilityDisplacement.WindowDurationSeconds(
                startFrame,
                endFrame,
                ability.FrameRate);
            instance.BeginDisplacement(
                direction,
                body,
                distance,
                duration,
                startFrame,
                endFrame);
        }

        /// <summary>
        /// Moves the owner through its displacement window. Travel is planar
        /// and kinematic (MovePosition, like root motion): velocity is never
        /// touched, and nothing is swept. Cancelling or completing the
        /// ability discards the instance, which stops travel immediately.
        /// </summary>
        private static void ApplyActiveDisplacement(AbilityInstance instance, float deltaTime)
        {
            if (instance == null ||
                !instance.HasActiveDisplacement ||
                instance.DisplacementBody == null ||
                !instance.IsDisplacementWindowOpen)
            {
                return;
            }

            if (!instance.TickDisplacement(deltaTime, out Vector3 step))
            {
                return;
            }

            Rigidbody body = instance.DisplacementBody;
            body.MovePosition(body.position + step);
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
