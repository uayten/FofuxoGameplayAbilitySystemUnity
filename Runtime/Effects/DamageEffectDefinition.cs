using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// Where the push comes from. Owner forward is a directed swing, away from
    /// owner fans hits outward from the attacker, away from centre is what an
    /// explosion does.
    /// </summary>
    public enum KnockbackDirection
    {
        OwnerForward,
        AwayFromOwner,
        AwayFromShapeCenter
    }

    /// <summary>
    /// Deals damage to every actor the effect targeting accepts. One effect
    /// covers melee arcs, boxes, capsules and explosions: the shape says who is
    /// hit, the fields below say what the hit does. The four per-shape effect
    /// classes this replaces differed only in the query, and had drifted apart
    /// by copy.
    ///
    /// Damage is a gameplay effect like any other: it builds a spec, it obeys
    /// application tags and immunity, and its magnitude is captured from the
    /// source under the same rules a buff uses. The number is subtracted from
    /// the target's <see cref="TargetAttribute"/> — health, poise, whatever the
    /// game calls it — and then the target is asked to react: a gameplay event,
    /// <see cref="ReactionEventTag"/>, goes to its ability system with this very
    /// application attached, and the target's own hit-reaction ability owns the
    /// animation, the control lock and the knockback from there. Nothing is
    /// handed to a consumer callback, and the decision to react — i-frames,
    /// guard, death — is made on the target, where the tags live.
    /// </summary>
    [CreateAssetMenu(
        fileName = "GE_Damage",
        menuName = "Fofuxo/Abilities/Effects/Damage")]
    public sealed class DamageEffectDefinition : GameplayEffectDefinition
    {
        [Header("Damage")]
        [Tooltip("The attribute the damage is subtracted from: the target's health, or its poise for a stagger meter. The target needs an AttributeSet holding it; an actor without one is not a damage target.")]
        [SerializeField] private GameplayAttribute targetAttribute;
        [Tooltip("Damage dealt to each accepted target, before falloff.")]
        [SerializeField] private GameplayEffectMagnitude damage = new(1f);
        [Tooltip("Fades damage to zero at the edge of the shape. Explosions use this; a sword swing usually does not.")]
        [SerializeField] private bool linearFalloff;

        [Header("Knockback")]
        [Tooltip("The push this hit authors. The target's reaction ability applies it, scaled by its own Knockback Scale, as a movement task in its own scope.")]
        [SerializeField] private KnockbackDirection knockbackDirection = KnockbackDirection.OwnerForward;
        [SerializeField, Min(0f)] private float horizontalKnockback;
        [SerializeField] private float verticalKnockback;
        [SerializeField, Min(0f)] private float knockbackDuration;

        [Header("Reaction")]
        [Tooltip("Gameplay event sent to the target's ability system once the damage has landed, so its own hit-reaction ability decides what happens next. Event.HitReaction for a hit, Event.Knockdown for one that floors; empty sends nothing, for a damage tick that should not flinch.")]
        [SerializeField] private GameplayTag reactionEventTag = new("Event.HitReaction");
        [Tooltip("A target holding State.Parrying refuses this hit, and its effect container raises EffectBlocked for the parry to reward. Off, the hit lands through a parry window.")]
        [SerializeField] private bool canBeParried = true;

        public GameplayAttribute TargetAttribute => targetAttribute;
        public GameplayEffectMagnitude Damage => damage;
        public GameplayTag ReactionEventTag => reactionEventTag;
        public bool CanBeParried => canBeParried;

        /// <summary>True when the hit authors a push at all.</summary>
        public bool HasKnockback =>
            knockbackDuration > 0f &&
            (horizontalKnockback > 0f || !Mathf.Approximately(verticalKnockback, 0f));

        /// <summary>
        /// Damage this application deals to this target, falloff included.
        /// Reads the spec, never the asset, so two targets of one swing can take
        /// different numbers without either being written down anywhere shared.
        /// </summary>
        public int ResolveDamage(GameplayEffectSpec spec)
        {
            int amount = Mathf.Max(1, Mathf.RoundToInt(damage.Evaluate(spec)));
            if (!linearFalloff || spec.Source == null || !spec.HasHit)
            {
                return amount;
            }

            Vector3 center = Targeting.Shape.ResolveCenter(
                spec.Source.transform, spec.Context.AimPoint);
            float reach = Targeting.Shape.Reach();
            if (reach <= Mathf.Epsilon)
            {
                return amount;
            }

            Vector3 targetPosition = spec.Target != null
                ? spec.Target.transform.position
                : spec.Hit.Point;
            float fade = 1f - Mathf.Clamp01(Vector3.Distance(targetPosition, center) / reach);
            return Mathf.Max(1, Mathf.RoundToInt(amount * fade));
        }

        public override bool TryValidate(out string error)
        {
            if (!base.TryValidate(out error))
            {
                return false;
            }

            if (targetAttribute.IsEmpty)
            {
                error =
                    "Target Attribute is empty, so the damage has nothing to " +
                    "subtract from. Name the target's health attribute.";
                return false;
            }

            if (!damage.CapturesAttribute && damage.BaseValue <= 0f)
            {
                error =
                    "Damage must be greater than zero. A hit that deals nothing " +
                    "still consumes the trigger and still counts as a hit.";
                return false;
            }

            if (linearFalloff && Targeting.Mode != GameplayEffectTargetMode.Shape)
            {
                error =
                    "Linear falloff measures the distance from the shape centre, " +
                    "and this effect does not use Shape targeting, so it fades " +
                    "nothing. Switch to Shape, or turn falloff off.";
                return false;
            }

            if (knockbackDuration > 0f &&
                horizontalKnockback <= 0f &&
                Mathf.Approximately(verticalKnockback, 0f))
            {
                error =
                    "A knockback duration is authored but both knockback values " +
                    "are zero, so the target is pushed nowhere for that long.";
                return false;
            }

            error = null;
            return true;
        }

        protected override void CaptureOwnMagnitudes(GameplayEffectSpec spec)
        {
            spec.CaptureMagnitude(damage);
        }

        /// <summary>
        /// Only actors that hold attributes are hit: the damage lands on an
        /// <see cref="AttributeSet"/>, so a collider with none behind it is
        /// scenery to this effect. The set is also the per-trigger hit key, so
        /// two colliders of one actor count once.
        /// </summary>
        protected override bool TryResolveTarget(
            in AbilityTargetHit hit,
            in GameplayEffectContext context,
            out GameObject actor,
            out Object dedupKey)
        {
            actor = null;
            dedupKey = null;
            if (hit.Actor == null)
            {
                return false;
            }

            AttributeSet attributes = hit.Actor.GetComponentInParent<AttributeSet>();
            if (attributes == null)
            {
                return false;
            }

            actor = attributes.gameObject;
            dedupKey = attributes;
            return true;
        }

        /// <summary>
        /// The states the package itself names refuse damage at the target:
        /// dead, invulnerable, and parrying when the hit can be parried. They are
        /// rules of the kind, not of one asset, so an author cannot forget them
        /// on the eleventh damage effect; the authored Application Blocked Tags
        /// add to them and nothing removes them. A refusal here raises the
        /// target container's <c>EffectBlocked</c> like any other, which is the
        /// seam a parry reward hangs on.
        /// </summary>
        protected internal override bool IsBlockedOn(GameplayEffectContainer target)
        {
            return target.HasTag(CommonGameplayTags.Dead) ||
                   target.HasTag(CommonGameplayTags.Invulnerable) ||
                   (canBeParried && target.HasTag(CommonGameplayTags.Parrying));
        }

        /// <summary>A refusal by a parrying target is a parry; every other one is a block.</summary>
        protected internal override GameplayCueOutcome ClassifyRefusal(GameplayEffectContainer target)
        {
            return canBeParried && target.HasTag(CommonGameplayTags.Parrying)
                ? GameplayCueOutcome.Parried
                : GameplayCueOutcome.Blocked;
        }

        /// <summary>The cue carries the damage dealt, falloff included.</summary>
        protected internal override float ResolveCueMagnitude(GameplayEffectSpec spec)
        {
            return ResolveDamage(spec);
        }

        /// <summary>
        /// Subtracts the damage from the target attribute, then asks the target
        /// to react. The order is the point: a target whose health reached zero
        /// has already raised its death by the time the reaction is requested,
        /// so the request meets <c>State.Dead</c> and is refused — by the target.
        /// A periodic tick deals its damage and requests nothing; a burn does
        /// not flinch every quarter second.
        /// </summary>
        protected override bool Execute(GameplayEffectSpec spec, bool periodic)
        {
            if (spec.Source == null || spec.Target == null || targetAttribute.IsEmpty)
            {
                return false;
            }

            AttributeSet attributes = spec.Target.GetComponent<AttributeSet>();
            if (attributes == null)
            {
                return false;
            }

            int amount = ResolveDamage(spec);
            attributes.ApplyInstantModifier(new AttributeModifier(
                targetAttribute, AttributeOperation.Add, -amount, spec.Source));

            if (!periodic)
            {
                RequestReaction(spec);
            }

            return true;
        }

        /// <summary>
        /// The target-owned half. The event goes through the target's own
        /// activation rules with this application attached, so its reaction
        /// ability reads the hit off <see cref="AbilityInstance.TriggeringSpec"/>.
        /// Whether anything answers is the target's business: the damage has
        /// landed either way.
        /// </summary>
        private void RequestReaction(GameplayEffectSpec spec)
        {
            if (reactionEventTag.IsEmpty)
            {
                return;
            }

            AbilitySystem targetSystem = spec.Target.GetComponent<AbilitySystem>();
            if (targetSystem == null)
            {
                return;
            }

            AbilityContext reactionContext = new(
                spec.Target,
                spec.Source,
                ResolvePushDirection(spec),
                spec.HasHit ? spec.Hit.Point : spec.Target.transform.position);
            targetSystem.TryHandleGameplayEvent(reactionEventTag, reactionContext, spec, out _);
        }

        /// <summary>
        /// The push this hit authored, resolved against where the target stands
        /// now: planar direction by the knockback policy, magnitude and lift as
        /// authored. The reaction ability scales and runs it.
        /// </summary>
        protected internal override bool TryGetKnockback(
            GameplayEffectSpec spec, out Vector3 velocity, out float duration)
        {
            velocity = Vector3.zero;
            duration = knockbackDuration;
            if (duration <= 0f || spec.Source == null || spec.Target == null)
            {
                return false;
            }

            velocity = ResolvePushDirection(spec) * horizontalKnockback +
                       Vector3.up * verticalKnockback;
            return velocity.sqrMagnitude > Mathf.Epsilon;
        }

        private Vector3 ResolvePushDirection(GameplayEffectSpec spec)
        {
            Transform owner = spec.Source.transform;
            Vector3 center = Targeting.Shape.ResolveCenter(owner, spec.Context.AimPoint);
            return ResolvePlanar(
                ResolveHitDirection(owner, center, spec.Target.transform.position), owner);
        }

        private Vector3 ResolveHitDirection(
            Transform owner, Vector3 center, Vector3 targetPosition)
        {
            switch (knockbackDirection)
            {
                case KnockbackDirection.AwayFromOwner:
                    return targetPosition - owner.position;
                case KnockbackDirection.AwayFromShapeCenter:
                    return targetPosition - center;
                default:
                    return owner.forward;
            }
        }

        /// <summary>
        /// Knockback stays on the ground plane; the vertical component is
        /// authored separately so a launcher is a deliberate choice.
        /// </summary>
        private static Vector3 ResolvePlanar(Vector3 direction, Transform owner)
        {
            Vector3 planar = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (planar.sqrMagnitude <= Mathf.Epsilon)
            {
                planar = Vector3.ProjectOnPlane(owner.forward, Vector3.up);
            }

            if (planar.sqrMagnitude <= Mathf.Epsilon)
            {
                return owner.forward;
            }

            return planar.normalized;
        }

        internal void ConfigureDamageForTests(
            float amount,
            GameplayAttribute attribute = default,
            bool falloff = false,
            float horizontal = 0f,
            float vertical = 0f,
            float duration = 0f,
            bool parryable = true,
            GameplayAttribute scaleAttribute = default,
            float scaleFactor = 0f)
        {
            targetAttribute = attribute;
            damage = new GameplayEffectMagnitude(amount, scaleAttribute, scaleFactor);
            linearFalloff = falloff;
            horizontalKnockback = horizontal;
            verticalKnockback = vertical;
            knockbackDuration = duration;
            canBeParried = parryable;
        }

        internal void SetKnockbackDirectionForTests(KnockbackDirection value)
        {
            knockbackDirection = value;
        }

        internal void SetReactionEventTagForTests(GameplayTag tag)
        {
            reactionEventTag = tag;
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            damage.Sanitize();
            horizontalKnockback = Mathf.Max(0f, horizontalKnockback);
            knockbackDuration = Mathf.Max(0f, knockbackDuration);
        }
    }
}
