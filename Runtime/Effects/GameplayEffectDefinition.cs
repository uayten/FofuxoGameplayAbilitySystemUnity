using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// Immutable authoring data for one gameplay effect: who it reaches, how
    /// long it lasts, which attributes it changes, how it stacks, and which tags
    /// it grants, requires, blocks, makes the target immune to, and removes.
    ///
    /// A definition holds no runtime state. Every application produces a
    /// <see cref="GameplayEffectSpec"/> — the per-use data — and a duration or
    /// infinite one produces an <see cref="ActiveGameplayEffect"/> named by a
    /// stable handle. Applying the same asset a hundred times leaves the asset
    /// byte-identical.
    ///
    /// The class is concrete: an effect that only changes attributes and tags
    /// needs no subclass. Subclasses exist for effects that also do something
    /// the attribute layer cannot express — dealing a hit through the game
    /// damage contract, pushing a body through physics, drawing a volume.
    /// </summary>
    [CreateAssetMenu(
        fileName = "GE_NewEffect",
        menuName = "Fofuxo/Abilities/Effects/Gameplay Effect")]
    public class GameplayEffectDefinition : ScriptableObject
    {
        /// <summary>
        /// Acquisition buffer for an effect applied outside an activation, where
        /// there is no per-frame buffer to share. Static, never per asset: a
        /// definition that stored an acquisition would be runtime state on disk.
        /// </summary>
        private static readonly AbilityTargetData DetachedTargets = new();

        /// <summary>
        /// Shared, stateless definitions backing the code-driven applications
        /// that have no asset — keyed by the two things such a caller chooses.
        /// </summary>
        private static readonly Dictionary<int, GameplayEffectDefinition> DynamicDefinitions =
            new();

        [Header("Identity")]
        [Tooltip("Stable id a save record uses to find this effect again. Only an effect that outlives the frame it lands on needs one; an instant effect is over before anything could be saved.")]
        [SerializeField] private string effectId;

        [Header("Application")]
        [SerializeField] private GameplayEffectTargeting targeting = GameplayEffectTargeting.AbilityTarget;

        [Header("Lifecycle")]
        [SerializeField]
        private GameplayEffectDurationPolicy durationPolicy = GameplayEffectDurationPolicy.Instant;
        [Tooltip("Seconds the effect lasts. Only read for the Duration policy.")]
        [SerializeField] private GameplayEffectMagnitude duration = new(1f);
        [Tooltip("Seconds between executions. Zero applies the modifiers once, as live modifiers instead of repeated base changes.")]
        [SerializeField] private GameplayEffectMagnitude period;
        [Tooltip("Run the first period immediately instead of one period later.")]
        [SerializeField] private bool executePeriodOnApplication = true;

        [Header("Modifiers")]
        [SerializeField] private GameplayEffectModifier[] modifiers = { };

        [Header("Stacking")]
        [SerializeField] private GameplayEffectStacking stacking = GameplayEffectStacking.Default;

        [Header("Tags")]
        [Tooltip("Identifies this effect. Removal and immunity name effects by these tags, never by asset.")]
        [SerializeField] private GameplayTag[] effectTags = { };
        [Tooltip("Granted to the target while the effect is active.")]
        [SerializeField] private GameplayTag[] grantedTags = { };
        [Tooltip("The target must hold all of these for the effect to land.")]
        [SerializeField] private GameplayTag[] applicationRequiredTags = { };
        [Tooltip("The target holding any of these refuses the effect.")]
        [SerializeField] private GameplayTag[] applicationBlockedTags = { };
        [Tooltip("While active, the target is immune to effects carrying any of these effect tags.")]
        [SerializeField] private GameplayTag[] grantedImmunityTags = { };
        [Tooltip("On application, removes the target's active effects carrying any of these effect tags.")]
        [SerializeField] private GameplayTag[] removeEffectsWithTags = { };

        [Header("Cue")]
        [Tooltip("Cosmetic cue raised on the target when this effect lands: executed once for an instant effect, added for a duration or infinite one and removed with it. Empty raises nothing.")]
        [SerializeField] private GameplayTag cueTag;
        [Tooltip("What to raise instead when the application did not land — blocked by the target's tags, immune, or parried. Empty suppresses the cue for that outcome; an outcome with no entry raises nothing either.")]
        [SerializeField] private GameplayCueReplacement[] cueReplacements = { };

        /// <summary>
        /// Stable identifier for persistence, empty when the effect was never
        /// given one. An instant effect needs none: it is over before a record
        /// could carry it. A duration or infinite effect without an id is
        /// reported by <see cref="AbilityPersistence.Capture(AbilitySystem)"/>
        /// and left out of the record, because a saved effect nothing can
        /// resolve is worse than a named gap.
        /// </summary>
        public string EffectId => effectId ?? string.Empty;

        public GameplayEffectTargeting Targeting => targeting;
        public GameplayTag CueTag => cueTag;
        public IReadOnlyList<GameplayCueReplacement> CueReplacements => cueReplacements;
        public GameplayEffectDurationPolicy DurationPolicy => durationPolicy;
        public GameplayEffectMagnitude Duration => duration;
        public GameplayEffectMagnitude Period => period;
        public bool ExecutePeriodOnApplication => executePeriodOnApplication;
        public IReadOnlyList<GameplayEffectModifier> Modifiers => modifiers;
        public GameplayEffectStacking Stacking => stacking;
        public IReadOnlyList<GameplayTag> EffectTags => effectTags;
        public IReadOnlyList<GameplayTag> GrantedTags => grantedTags;
        public IReadOnlyList<GameplayTag> ApplicationRequiredTags => applicationRequiredTags;
        public IReadOnlyList<GameplayTag> ApplicationBlockedTags => applicationBlockedTags;
        public IReadOnlyList<GameplayTag> GrantedImmunityTags => grantedImmunityTags;
        public IReadOnlyList<GameplayTag> RemoveEffectsWithTags => removeEffectsWithTags;

        public bool HasAnyEffectTag(IReadOnlyList<GameplayTag> tags)
        {
            if (tags == null)
            {
                return false;
            }

            for (int i = 0; i < effectTags.Length; i++)
            {
                for (int j = 0; j < tags.Count; j++)
                {
                    if (!effectTags[i].IsEmpty && effectTags[i] == tags[j])
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Whether this asset is authored well enough to do anything at all.
        /// It runs from the Inspector, and from the ability that fires it: an
        /// <see cref="AbilityStep"/> validates every effect its triggers reach,
        /// so a broken effect is reported by the ability that would have fired
        /// it and not only by its own asset.
        ///
        /// What it looks for is the class of mistake that produces silence
        /// rather than an error — a modifier pointing at no attribute, an
        /// attribute folded in with a coefficient of zero, a duration effect
        /// with no duration, a shape that queries every layer in the project.
        /// A subclass overrides it, checks its own fields, and calls the base
        /// first.
        /// </summary>
        public virtual bool TryValidate(out string error)
        {
            for (int i = 0; i < modifiers.Length; i++)
            {
                GameplayEffectModifier modifier = modifiers[i];
                if (modifier.Attribute.IsEmpty)
                {
                    error = $"Modifier {i + 1} has no attribute assigned.";
                    return false;
                }

                GameplayEffectMagnitude magnitude = modifier.Magnitude;
                if (!magnitude.Attribute.IsEmpty && magnitude.Coefficient == 0f)
                {
                    error =
                        $"Modifier {i + 1} scales by '{magnitude.Attribute}' with a " +
                        "coefficient of zero, so the attribute is read and thrown " +
                        "away. Set a coefficient, or clear the attribute to make " +
                        "the magnitude flat.";
                    return false;
                }
            }

            if (!TryValidateLifecycle(out error))
            {
                return false;
            }

            if (targeting.Mode == GameplayEffectTargetMode.Shape &&
                targeting.Shape.TargetLayerMask == 0)
            {
                error =
                    "Target Layers is empty, and an empty mask queries every layer " +
                    "in the project — terrain, triggers and the owner included. " +
                    "Pick the layers damageable actors live on.";
                return false;
            }

            if (!TryValidateTagList(effectTags, "Effect", out error) ||
                !TryValidateTagList(grantedTags, "Granted", out error) ||
                !TryValidateTagList(applicationRequiredTags, "Application required", out error) ||
                !TryValidateTagList(applicationBlockedTags, "Application blocked", out error) ||
                !TryValidateTagList(grantedImmunityTags, "Granted immunity", out error) ||
                !TryValidateTagList(removeEffectsWithTags, "Remove effects with", out error))
            {
                return false;
            }

            for (int i = 0; i < cueReplacements.Length; i++)
            {
                for (int j = 0; j < i; j++)
                {
                    if (cueReplacements[j].Outcome == cueReplacements[i].Outcome)
                    {
                        error =
                            $"Cue replacements {j + 1} and {i + 1} both answer the " +
                            $"{cueReplacements[i].Outcome} outcome. One entry per outcome.";
                        return false;
                    }
                }
            }

            return TryValidateOverflow(out error);
        }

        /// <summary>
        /// Duration and period against the policy that reads them. Each policy
        /// ignores the fields it does not use, so a value authored under the
        /// wrong one is never applied and never reported.
        /// </summary>
        private bool TryValidateLifecycle(out string error)
        {
            if (durationPolicy == GameplayEffectDurationPolicy.Duration &&
                !duration.CapturesAttribute && duration.BaseValue <= 0f)
            {
                error =
                    "A Duration effect needs a duration greater than zero. Use the " +
                    "Instant policy for a one-shot change, or Infinite for one that " +
                    "is removed by hand.";
                return false;
            }

            if (period.BaseValue < 0f)
            {
                error = "Period cannot be negative.";
                return false;
            }

            if (durationPolicy == GameplayEffectDurationPolicy.Instant &&
                (period.BaseValue > 0f || period.CapturesAttribute))
            {
                error =
                    "An Instant effect executes once and has no period. Use the " +
                    "Duration or Infinite policy to run it repeatedly.";
                return false;
            }

            error = null;
            return true;
        }

        /// <summary>
        /// Overflow effects only run when a stack can actually fill, and an
        /// effect that overflows into itself would apply forever.
        /// </summary>
        private bool TryValidateOverflow(out string error)
        {
            IReadOnlyList<GameplayEffectDefinition> overflow = stacking.OverflowEffects;
            for (int i = 0; i < overflow.Count; i++)
            {
                if (overflow[i] == null)
                {
                    error = $"Overflow effect {i + 1} has no effect assigned.";
                    return false;
                }

                if (overflow[i] == this)
                {
                    error =
                        $"Overflow effect {i + 1} is this effect, so overflowing " +
                        "would apply it again without end.";
                    return false;
                }
            }

            if (overflow.Count > 0 && stacking.Limit <= 0)
            {
                error =
                    "Overflow effects are authored but the stack limit is zero, " +
                    "which never overflows. Set a limit, or clear the overflow list.";
                return false;
            }

            error = null;
            return true;
        }

        private static bool TryValidateTagList(
            GameplayTag[] tags, string label, out string error)
        {
            for (int i = 0; tags != null && i < tags.Length; i++)
            {
                if (tags[i].IsEmpty)
                {
                    error = $"{label} tag {i + 1} is empty.";
                    return false;
                }
            }

            error = null;
            return true;
        }

        /// <summary>
        /// Applies this effect from an ability timeline: resolve the targets,
        /// build one spec per target, and hand each to that actor container.
        ///
        /// In <see cref="GameplayEffectTargetMode.Shape"/> mode the acquisition
        /// comes from the activation, so two effects on the same frame of the
        /// same step with the same shape share one physics query and therefore
        /// reach the same actors.
        /// </summary>
        /// <returns>How many targets accepted the effect.</returns>
        public int ApplyFrom(in GameplayEffectContext context)
        {
            if (context.Owner == null)
            {
                return 0;
            }

            if (targeting.Mode != GameplayEffectTargetMode.Shape)
            {
                GameObject actor = targeting.Mode == GameplayEffectTargetMode.Owner
                    ? context.Owner
                    : context.Target != null
                        ? context.Target
                        : context.Owner;
                bool delivered = ApplyToActor(actor, default, in context);
                RecordDelivery(in context, actor, delivered);
                return delivered ? 1 : 0;
            }

            AbilityContext abilityContext = context.AbilityContext;
            AbilityTargetData targets = context.Instance != null
                ? context.Instance.AcquireTargets(
                    targeting.Shape, abilityContext.AimPoint, abilityContext.Direction)
                : AcquireDetached(context.Owner, abilityContext);
            OnTargetsAcquired(targets, in context);
            AbilityTargetFilter filter = targeting.ResolveFilter(context.Target);
            int limit = targeting.MaximumTargets;
            int accepted = 0;

            for (int i = 0; i < targets.Count && accepted < limit; i++)
            {
                AbilityTargetHit hit = targets[i];
                if (!filter.Accepts(in hit, context.Owner, targets.Origin) ||
                    !TryResolveTarget(in hit, in context, out GameObject actor, out UnityEngine.Object key))
                {
                    continue;
                }

                if (context.Instance != null &&
                    !context.Instance.TryRegisterHit(context.TriggerIndex, key))
                {
                    continue;
                }

                bool delivered = ApplyToActor(actor, in hit, in context);
                RecordDelivery(in context, actor, delivered);
                if (delivered)
                {
                    accepted++;
                }
            }

            return accepted;
        }

        /// <summary>
        /// The source-side record of a hit: this effect reached that actor, and
        /// whether the actor took it. The target writes its own half from its
        /// container, so one hit reads from both ends.
        /// </summary>
        private void RecordDelivery(in GameplayEffectContext context, GameObject actor, bool accepted)
        {
            if (AbilityDiagnostics.Enabled && context.AbilitySystem != null)
            {
                context.AbilitySystem.Record(
                    AbilityEvent.Delivered(context.Definition, this, actor, accepted));
            }
        }

        /// <summary>
        /// Builds the spec for one target and applies it. Everything an
        /// application varies by lives on the spec, so this is the only place a
        /// target-specific value is ever produced.
        /// </summary>
        protected bool ApplyToActor(
            GameObject actor, in AbilityTargetHit hit, in GameplayEffectContext context)
        {
            if (actor == null)
            {
                return false;
            }

            GameplayEffectContainer container = GameplayEffectContainer.For(actor);
            if (container == null)
            {
                return false;
            }

            GameplayEffectSpec spec = new(this, in context, actor, in hit);
            return container.Apply(spec).Succeeded;
        }

        /// <summary>
        /// The acquisition this application will read, before any filtering.
        /// A seam for diagnostics and tests; the shipping effects do not use it.
        /// </summary>
        protected virtual void OnTargetsAcquired(
            AbilityTargetData targets, in GameplayEffectContext context)
        {
        }

        /// <summary>
        /// Which actor a query hit delivers to, and what identifies it for the
        /// activation per-trigger hit registration. Subclasses narrow this to
        /// the component they actually need — a damage receiver, a physics body.
        /// </summary>
        protected virtual bool TryResolveTarget(
            in AbilityTargetHit hit,
            in GameplayEffectContext context,
            out GameObject actor,
            out UnityEngine.Object dedupKey)
        {
            actor = hit.Actor;
            dedupKey = hit.Actor;
            return actor != null;
        }

        /// <summary>
        /// States the target holds that refuse this effect beyond the authored
        /// Application Blocked Tags: the rules a subclass owns for its whole
        /// kind, so an author cannot forget them on one asset. Side-effect free,
        /// and a refusal here raises the container's <c>EffectBlocked</c> like
        /// any other.
        /// </summary>
        protected internal virtual bool IsBlockedOn(GameplayEffectContainer target)
        {
            return false;
        }

        /// <summary>
        /// The cue this effect raises for an outcome: its own tag when it
        /// landed, a replacement when the author wrote one for the outcome, and
        /// nothing otherwise. A refused application with no replacement is
        /// silent; a landed one with no tag is silent too.
        /// </summary>
        public bool TryResolveCue(GameplayCueOutcome outcome, out GameplayTag cue)
        {
            for (int i = 0; i < cueReplacements.Length; i++)
            {
                if (cueReplacements[i].Outcome == outcome)
                {
                    cue = cueReplacements[i].Cue;
                    return !cue.IsEmpty;
                }
            }

            cue = outcome == GameplayCueOutcome.Landed ? cueTag : default;
            return !cue.IsEmpty;
        }

        /// <summary>
        /// Which refusal the target's tags amount to, for the cue the effect
        /// raises instead. Immunity is classified by the container before this
        /// is asked; the base effect calls every other refusal a block, and a
        /// subclass with a finer vocabulary — damage knows a parry — says so.
        /// </summary>
        protected internal virtual GameplayCueOutcome ClassifyRefusal(GameplayEffectContainer target)
        {
            return GameplayCueOutcome.Blocked;
        }

        /// <summary>
        /// The number a cue carries as its magnitude: the first modifier's
        /// calculated magnitude, or the level when the effect has none. Damage
        /// answers with the damage it dealt.
        /// </summary>
        protected internal virtual float ResolveCueMagnitude(GameplayEffectSpec spec)
        {
            return spec.ModifierMagnitudes.Count > 0
                ? Mathf.Abs(spec.ModifierMagnitudes[0])
                : spec.Level;
        }

        /// <summary>
        /// The push this application carries, for the target's reaction ability
        /// to run as its own movement. False when the effect moves nobody, which
        /// is what the base effect says.
        /// </summary>
        protected internal virtual bool TryGetKnockback(
            GameplayEffectSpec spec, out Vector3 velocity, out float duration)
        {
            velocity = Vector3.zero;
            duration = 0f;
            return false;
        }

        /// <summary>
        /// Declares every magnitude this definition owns to the spec, so the
        /// snapshot table is filled before anything is evaluated.
        /// </summary>
        internal void CaptureMagnitudes(GameplayEffectSpec spec)
        {
            for (int i = 0; i < modifiers.Length; i++)
            {
                spec.CaptureMagnitude(modifiers[i].Magnitude);
            }

            spec.CaptureMagnitude(duration);
            spec.CaptureMagnitude(period);
            CaptureOwnMagnitudes(spec);
        }

        /// <summary>
        /// Magnitudes a subclass authors outside the modifier list — the damage
        /// number, for instance. Declaring them here is what puts them under the
        /// same source/target and snapshot/live rules.
        /// </summary>
        protected virtual void CaptureOwnMagnitudes(GameplayEffectSpec spec)
        {
        }

        /// <summary>
        /// The consequence this effect carries beyond its attribute modifiers.
        /// Runs once for an instant effect, and once per period for a periodic
        /// one, always before the modifiers of that same execution.
        /// </summary>
        /// <returns>
        /// False to refuse the whole application, the way a parried hit is
        /// refused: no modifiers, no tags, no active effect.
        /// </returns>
        protected virtual bool Execute(GameplayEffectSpec spec, bool periodic)
        {
            return true;
        }

        internal bool ExecuteEffect(GameplayEffectSpec spec, bool periodic)
        {
            return Execute(spec, periodic);
        }

        /// <summary>
        /// A shared, empty definition for a code-driven application: it declares
        /// only a duration policy and a stacking policy, and the spec carries
        /// the modifiers, the duration and the period. Cached, so a stun applied
        /// on every hit allocates nothing.
        /// </summary>
        internal static GameplayEffectDefinition Dynamic(
            GameplayEffectDurationPolicy policy, EffectStacking stackingPolicy)
        {
            int key = (int)policy * 16 + (int)stackingPolicy;
            if (DynamicDefinitions.TryGetValue(key, out GameplayEffectDefinition cached) &&
                cached != null)
            {
                return cached;
            }

            GameplayEffectDefinition definition = CreateInstance<GameplayEffectDefinition>();
            definition.name = $"GE_Dynamic_{policy}{stackingPolicy}";
            definition.hideFlags = HideFlags.HideAndDontSave;
            definition.durationPolicy = policy;
            definition.targeting = GameplayEffectTargeting.AbilityTarget;
            definition.stacking = GameplayEffectStacking.With(stackingPolicy);
            definition.executePeriodOnApplication = false;
            DynamicDefinitions[key] = definition;
            return definition;
        }

        private AbilityTargetData AcquireDetached(
            GameObject owner, in AbilityContext abilityContext)
        {
            AbilityTargetQuery.Acquire(
                targeting.Shape,
                owner,
                abilityContext.AimPoint,
                abilityContext.Direction,
                DetachedTargets);
            return DetachedTargets;
        }

        protected virtual void OnValidate()
        {
            effectId = effectId?.Trim();
            targeting.Sanitize();
            duration.Sanitize();
            period.Sanitize();
            effectTags ??= Array.Empty<GameplayTag>();
            grantedTags ??= Array.Empty<GameplayTag>();
        }

        internal void SetEffectIdForTests(string id)
        {
            effectId = id;
        }

        internal void ConfigureForTests(
            GameplayEffectDurationPolicy policy,
            GameplayEffectTargeting effectTargeting,
            GameplayEffectModifier[] effectModifiers,
            float durationSeconds = 0f,
            float periodSeconds = 0f)
        {
            durationPolicy = policy;
            targeting = effectTargeting;
            modifiers = effectModifiers ?? Array.Empty<GameplayEffectModifier>();
            duration = new GameplayEffectMagnitude(durationSeconds);
            period = new GameplayEffectMagnitude(periodSeconds);
        }

        internal void SetCueForTests(GameplayTag cue, params GameplayCueReplacement[] replacements)
        {
            cueTag = cue;
            cueReplacements = replacements ?? Array.Empty<GameplayCueReplacement>();
        }

        internal void SetStackingForTests(GameplayEffectStacking effectStacking)
        {
            stacking = effectStacking;
        }

        internal void SetTagsForTests(
            GameplayTag[] effect = null,
            GameplayTag[] granted = null,
            GameplayTag[] required = null,
            GameplayTag[] blocked = null,
            GameplayTag[] immunity = null,
            GameplayTag[] removal = null)
        {
            effectTags = effect ?? Array.Empty<GameplayTag>();
            grantedTags = granted ?? Array.Empty<GameplayTag>();
            applicationRequiredTags = required ?? Array.Empty<GameplayTag>();
            applicationBlockedTags = blocked ?? Array.Empty<GameplayTag>();
            grantedImmunityTags = immunity ?? Array.Empty<GameplayTag>();
            removeEffectsWithTags = removal ?? Array.Empty<GameplayTag>();
        }

        internal void SetDurationMagnitudeForTests(GameplayEffectMagnitude magnitude)
        {
            duration = magnitude;
        }

        internal void SetModifiersForTests(params GameplayEffectModifier[] effectModifiers)
        {
            modifiers = effectModifiers ?? Array.Empty<GameplayEffectModifier>();
        }

        internal void SetExecutePeriodOnApplicationForTests(bool value)
        {
            executePeriodOnApplication = value;
        }

        internal void SetTargetingForTests(GameplayEffectTargeting effectTargeting)
        {
            targeting = effectTargeting;
        }
    }
}
