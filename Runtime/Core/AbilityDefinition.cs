using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// How an ability moves from one step to the next. Automatic chains as soon
    /// as a step ends; the other three wait for something, and all three share
    /// the step's combo window — the window says *when* an advance may happen,
    /// the mode says *what* has to happen.
    ///
    /// The values of Automatic and Manual are fixed: they are serialized in
    /// authored assets, so anything new is appended.
    /// </summary>
    public enum AbilityStepAdvancement
    {
        /// <summary>Chains straight into the next step when a step ends.</summary>
        Automatic = 0,
        /// <summary>Waits for a buffered input inside the step's combo window.</summary>
        Manual = 1,
        /// <summary>
        /// Waits for a gameplay event carrying the ability's Step Advance Event
        /// tag. A scripted sequence driven by animation notifies, or a boss that
        /// chains on a landed hit, advances this way.
        /// </summary>
        OnEvent = 2,
        /// <summary>
        /// Polls a condition every tick while the window is open: the ability's
        /// own <see cref="AbilityDefinition.CanAdvanceStep"/> override, or the
        /// system's <c>StepAdvanceCondition</c> delegate when one is assigned.
        /// </summary>
        OnCondition = 3
    }

    /// <summary>
    /// A granted ability: everything decided once per activation — identity,
    /// targeting, range, cost, cooldown, charges, tags, exclusion, cancellation
    /// and AI weight — and nothing about how the action plays out.
    ///
    /// **There is no timeline here.** An ability of this type activates, runs
    /// whatever its derived type does in <see cref="OnActivated"/>, and stays
    /// active until something ends it — a call to
    /// <c>TryCompleteActiveAbility</c>, a cancel, or the owner going away. That
    /// is the shape for an ability whose animation is played elsewhere, that
    /// waits on a task, or that has no animation at all.
    ///
    /// An action authored as frame windows — a swing, a combo — is a
    /// <see cref="TimelineAbilityDefinition"/>, which is a kind of ability
    /// rather than the shape of every ability. That split is what keeps this
    /// type as generic as its Unreal counterpart: a montage there is something
    /// an ability plays, never a field every ability carries.
    ///
    /// An ability's identity is its asset, not its class. Derive a type for a
    /// new *kind* of ability, never one per skill.
    /// </summary>
    [CreateAssetMenu(fileName = "GA_NewAbility", menuName = "Fofuxo/Abilities/Ability")]
    public class AbilityDefinition : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string abilityId;

        [Header("Targeting")]
        [SerializeField] private bool requiresTarget = true;
        [SerializeField, Min(0f)] private float minimumRange;
        [SerializeField, Min(0f)] private float maximumRange = 3f;
        [SerializeField, Range(0f, 180f)] private float maximumFacingAngle = 180f;
        [Tooltip("Targeting data resolved on activation, before the animation: picks the enemy this ability commits to and may schedule startup approach movement.")]
        [FormerlySerializedAs("nestedAssist")]
        [SerializeField] private TargetAssistDefinition targetAssist;

        [Header("Activation")]
        [SerializeField, Min(0f)] private float cooldown;
        [SerializeField] private AbilityCooldownStartPolicy cooldownStartPolicy;
        [Tooltip("Which cancel requests may interrupt this ability.")]
        [SerializeField] private AbilityCancelPolicy cancelPolicy;
        [Tooltip("Cancel tags that interrupt this ability when the policy is Only Listed Tags. A game names its own reasons in the Cancel. namespace.")]
        [SerializeField] private GameplayTag[] cancelledByTags = { };
        [SerializeField] private bool lockMovementDuringAbility = true;
        [Tooltip("Which input edge activates this ability. The package never reads a device: whichever component owns the input reads this policy.")]
        [SerializeField] private AbilityActivationInputPolicy activationInputPolicy;
        [Tooltip("Seconds the input must be held. On Hold activates once it elapses; On Release requires at least this much before the release counts. Ignored by On Press.")]
        [SerializeField, Min(0f)] private float activationHoldDuration = 0.25f;

        [Header("Exclusion Group")]
        [Tooltip("Which group this ability belongs to for mutual exclusion. Empty means ungrouped, which is what the group-scoped policies below need in order to mean anything.")]
        [SerializeField] private GameplayTag groupTag;
        [Tooltip("What this activation does about the abilities already running. Block While Any Active is the one-ability-at-a-time rule; the others let abilities in different groups coexist.")]
        [SerializeField] private AbilityGroupExclusionPolicy groupExclusionPolicy;

        [Header("Automatic Activation")]
        [Tooltip("Events that may activate this ability while it is present in a loadout.")]
        [SerializeField] private AbilityActivationTrigger[] activationTriggers = { };

        [Header("Costs and Charges")]
        [SerializeField] private AbilityCost[] costs = { };
        [Tooltip("Charges available before the restore timer refills them. Zero means unlimited.")]
        [SerializeField, Min(0)] private int maxCharges;
        [Tooltip("Seconds to restore one charge. Zero restores all charges at once after the cooldown elapses.")]
        [SerializeField, Min(0f)] private float chargeRestoreTime;

        [Header("Gameplay Tags")]
        [SerializeField] private GameplayTag[] requiredTags = { };
        [SerializeField] private GameplayTag[] blockedTags = { };
        [SerializeField] private GameplayTag[] grantedTags = { };

        [Header("Reactive Effects")]
        [Tooltip("Applied to the owner on a successful parry while this ability is active (e.g. the block heal). The numbers live here, not in components.")]
        [SerializeField] private GameplayEffectDefinition[] onParryEffects = { };

        [Header("AI")]
        [SerializeField, Min(0f)] private float baseAiWeight = 1f;

        [Header("Preview")]
        [Tooltip("Editor-only clip played by the preview panel at the bottom of the Inspector. One per ability: the buttons under the step list point it at a step's clip. Never read by gameplay or included in a build.")]
        [SerializeField] private AnimationClip previewAnimationClip;

        public string AbilityId => abilityId ?? string.Empty;

        public bool RequiresTarget => requiresTarget;
        public float MinimumRange => Mathf.Max(0f, minimumRange);
        public float MaximumRange => Mathf.Max(MinimumRange, maximumRange);
        public float MaximumFacingAngle => Mathf.Clamp(maximumFacingAngle, 0f, 180f);
        public TargetAssistDefinition TargetAssist => targetAssist;

        /// <summary>
        /// True when activating this ability would move its owner — an
        /// activation-time target-assist approach, or any step with a
        /// displacement window or an approaching assist of its own.
        ///
        /// The ability cannot decide whether that is possible: a motor belongs
        /// to the actor. <c>AbilitySystem</c> reads this and refuses the
        /// activation with <see cref="AbilityActivationRejection.MissingMotor"/>
        /// when the owner has nowhere for the travel to land.
        /// </summary>
        public virtual bool RequiresMotor =>
            targetAssist != null && targetAssist.ApproachTarget;

        public float Cooldown => Mathf.Max(0f, cooldown);
        public AbilityCooldownStartPolicy CooldownStartPolicy => cooldownStartPolicy;
        public IReadOnlyList<AbilityCost> Costs => costs;
        public int MaxCharges => Mathf.Max(0, maxCharges);
        public float ChargeRestoreTime => Mathf.Max(0f, chargeRestoreTime);
        public bool HasLimitedCharges => MaxCharges > 0;
        public bool LockMovementDuringAbility => lockMovementDuringAbility;
        public AbilityActivationInputPolicy ActivationInputPolicy => activationInputPolicy;
        public float ActivationHoldDuration => Mathf.Max(0f, activationHoldDuration);
        public GameplayTag GroupTag => groupTag;
        public AbilityGroupExclusionPolicy GroupExclusionPolicy => groupExclusionPolicy;
        /// <summary>
        /// True when the exclusion policy is scoped by <see cref="GroupTag"/>
        /// rather than applying to every running ability.
        /// </summary>
        public bool UsesGroupScopedExclusion =>
            groupExclusionPolicy == AbilityGroupExclusionPolicy.BlockWhileSameGroupActive ||
            groupExclusionPolicy == AbilityGroupExclusionPolicy.CancelSameGroup;
        /// <summary>
        /// True when this ability and <paramref name="other"/> sit in the same
        /// non-empty exclusion group.
        /// </summary>
        public bool SharesGroupWith(AbilityDefinition other) =>
            other != null && !groupTag.IsEmpty && groupTag == other.groupTag;
        public IReadOnlyList<AbilityActivationTrigger> ActivationTriggers =>
            activationTriggers ?? Array.Empty<AbilityActivationTrigger>();

        public IReadOnlyList<GameplayTag> RequiredTags => requiredTags;
        public IReadOnlyList<GameplayTag> BlockedTags => blockedTags;
        public IReadOnlyList<GameplayTag> GrantedTags => grantedTags;
        public IReadOnlyList<GameplayEffectDefinition> OnParryEffects => onParryEffects;
        public float BaseAiWeight => Mathf.Max(0f, baseAiWeight);

        /// <summary>
        /// Editor-only. The preview panel plays this clip and never the
        /// gameplay clip of a step, so a placeholder swing can be inspected
        /// without touching what the ability actually plays.
        /// </summary>
        public AnimationClip PreviewClip => previewAnimationClip;
        public bool HasAnimationPreview => previewAnimationClip != null;

        public AbilityCancelPolicy CancelPolicy => cancelPolicy;
        public IReadOnlyList<GameplayTag> CancelledByTags =>
            cancelledByTags ?? Array.Empty<GameplayTag>();

        /// <summary>
        /// Whether a cancel request carrying <paramref name="cancelTag"/> may
        /// interrupt this ability. A forced cancellation ignores this: death and
        /// teardown must always be able to stop an activation.
        /// </summary>
        public bool CanBeCancelledBy(GameplayTag cancelTag)
        {
            switch (cancelPolicy)
            {
                case AbilityCancelPolicy.Nothing:
                    return false;
                case AbilityCancelPolicy.OnlyListedTags:
                    break;
                default:
                    return true;
            }

            IReadOnlyList<GameplayTag> allowed = CancelledByTags;
            for (int i = 0; i < allowed.Count; i++)
            {
                if (allowed[i] == cancelTag)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Whether the owner may move while this activation runs. The
        /// ability-level flag is the whole rule here; a timeline overrides it to
        /// let a step hand movement back partway through.
        /// </summary>
        public virtual bool IsMovementLockedDuring(AbilityInstance instance)
        {
            return LockMovementDuringAbility;
        }

        /// <summary>
        /// Lifecycle hooks for a derived ability. The base does nothing, and the
        /// system calls these before it raises the matching public event, so an
        /// ability's own logic runs before anything listening from outside sees
        /// the change.
        ///
        /// These are the extension point for behaviour a data field cannot
        /// express. Reusable consequences belong in an
        /// <see cref="GameplayEffectDefinition"/> instead: one effect asset serves
        /// every ability, while a hook only serves this type.
        ///
        /// Never store per-activation state on the definition — it is a shared
        /// asset. Write it on <paramref name="instance"/> or on the owner.
        /// </summary>
        protected internal virtual void OnActivated(AbilityInstance instance)
        {
        }

        /// <summary>The ability reached the end of its last step.</summary>
        protected internal virtual void OnCompleted(AbilityInstance instance)
        {
        }

        /// <summary>The ability was interrupted, carrying the request's tag.</summary>
        protected internal virtual void OnCancelled(AbilityInstance instance, GameplayTag cancelTag)
        {
        }

        public virtual bool TryValidate(out string error)
        {
            if (string.IsNullOrWhiteSpace(AbilityId))
            {
                error = "Ability ID is required.";
                return false;
            }

            if (UsesGroupScopedExclusion && groupTag.IsEmpty)
            {
                error =
                    "The exclusion policy is scoped by group but no Group Tag is set. " +
                    "Use Block While Any Active for an ungrouped ability.";
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

            if (cancelPolicy == AbilityCancelPolicy.OnlyListedTags)
            {
                IReadOnlyList<GameplayTag> allowed = CancelledByTags;
                if (allowed.Count == 0)
                {
                    error =
                        "Cancel policy is Only Listed Tags but no tag is listed. " +
                        "Use Nothing to make the ability uninterruptible.";
                    return false;
                }

                for (int i = 0; i < allowed.Count; i++)
                {
                    if (allowed[i].IsEmpty)
                    {
                        error = $"Cancel tag {i + 1} is empty.";
                        return false;
                    }
                }
            }

            IReadOnlyList<AbilityActivationTrigger> triggers = ActivationTriggers;
            for (int i = 0; i < triggers.Count; i++)
            {
                if (triggers[i].Tag.IsEmpty)
                {
                    error = $"Activation trigger {i + 1} has no tag assigned.";
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

                if (!onParryEffects[i].TryValidate(out string parryError))
                {
                    error =
                        $"Parry effect {i + 1} ('{onParryEffects[i].name}') " +
                        $"is invalid: {parryError}";
                    return false;
                }
            }

            if (targetAssist != null && !targetAssist.TryValidate(out error))
            {
                error = "Target assist is invalid: " + error;
                return false;
            }

            error = null;
            return true;
        }

        /// <summary>
        /// Authoring advice that must not stop the ability from running. An
        /// effect scheduled after the combo window still fires whenever the
        /// player does not continue the combo, so this is a warning and never a
        /// validation failure — making it fatal once silently disabled an
        /// entire boss combo.
        /// </summary>
        /// <summary>
        /// Something an author should look at, short of a validation error. A
        /// derived type adds its own by overriding this and calling back into
        /// the base for the ones that apply to every ability.
        /// </summary>
        public virtual bool TryGetAuthoringWarning(out string warning)
        {
            // Nothing here can end it: the base class runs no code of its own,
            // and without a timeline there is no last frame to complete on. It
            // would hold its granted tags and its exclusion until something
            // cancelled it.
            if (GetType() == typeof(AbilityDefinition))
            {
                warning =
                    "This ability has no steps and no subclass, so nothing will " +
                    "ever end it: it stays active until something cancels it. " +
                    "Derive an ability type that completes itself, or use a " +
                    "Timeline Ability.";
                return true;
            }

            warning = null;
            return false;
        }

        protected virtual void OnValidate()
        {
            abilityId = abilityId?.Trim();
            minimumRange = Mathf.Max(0f, minimumRange);
            maximumRange = Mathf.Max(minimumRange, maximumRange);
            cooldown = Mathf.Max(0f, cooldown);
            chargeRestoreTime = Mathf.Max(0f, chargeRestoreTime);
            maxCharges = Mathf.Max(0, maxCharges);
            activationHoldDuration = Mathf.Max(0f, activationHoldDuration);
            baseAiWeight = Mathf.Max(0f, baseAiWeight);

        }

        internal void SetAbilityIdForTests(string id)
        {
            abilityId = id;
        }

        internal void SetPreviewClipForTests(AnimationClip clip)
        {
            previewAnimationClip = clip;
        }

        internal void SetTargetAssistForTests(TargetAssistDefinition assist)
        {
            targetAssist = assist;
        }

        internal void SetParryEffectsForTests(params GameplayEffectDefinition[] effects)
        {
            onParryEffects = effects ?? new GameplayEffectDefinition[0];
        }

        internal void SetCancellationForTests(
            AbilityCancelPolicy policy,
            params GameplayTag[] tags)
        {
            cancelPolicy = policy;
            cancelledByTags = tags ?? Array.Empty<GameplayTag>();
        }

        internal void SetActivationTriggersForTests(
            params AbilityActivationTrigger[] triggers)
        {
            activationTriggers = triggers ?? Array.Empty<AbilityActivationTrigger>();
        }

        internal void SetActivationInputPolicyForTests(
            AbilityActivationInputPolicy policy, float holdDuration)
        {
            activationInputPolicy = policy;
            activationHoldDuration = holdDuration;
        }

        internal void SetExclusionGroupForTests(
            GameplayTag group, AbilityGroupExclusionPolicy policy)
        {
            groupTag = group;
            groupExclusionPolicy = policy;
        }

        internal void SetRequiredTagsForTests(params GameplayTag[] tags)
        {
            requiredTags = tags ?? Array.Empty<GameplayTag>();
        }
    }
}
