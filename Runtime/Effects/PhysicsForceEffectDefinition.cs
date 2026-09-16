using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// Where a physics push sends the target.
    /// </summary>
    public enum PhysicsForceDirection
    {
        OwnerForward,
        AwayFromOwner,
        TowardOwner,
        AwayFromShapeCenter,
        TowardShapeCenter
    }

    /// <summary>
    /// Reusable push, pull, and launch effect. It reaches targets through the
    /// shared effect targeting, starts their event-triggered physics reaction
    /// ability, and applies a level-scaled velocity change through Unity
    /// physics.
    /// </summary>
    [CreateAssetMenu(
        fileName = "GE_PhysicsForce",
        menuName = "Fofuxo/Abilities/Effects/Physics Force")]
    public sealed class PhysicsForceEffectDefinition : GameplayEffectDefinition
    {

        [Header("Reaction")]
        [SerializeField] private GameplayTag eventTag = new("Event.PhysicsForce");
        [SerializeField] private PhysicsForceDirection direction = PhysicsForceDirection.OwnerForward;
        [SerializeField] private PhysicsForceApplication application = PhysicsForceApplication.ReplaceVelocity;

        [Header("Level Scaling (1-100)")]
        [SerializeField] private AnimationCurve horizontalVelocityByLevel =
            new(new Keyframe(1f, 1f), new Keyframe(100f, 25f));
        [SerializeField] private AnimationCurve verticalVelocityByLevel =
            new(new Keyframe(1f, 0f), new Keyframe(100f, 12f));

        public GameplayTag EventTag => eventTag;
        public PhysicsForceDirection Direction => direction;

        /// <summary>Only actors carrying a physics body, and never the owner.</summary>
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

            AbilityPhysicsBody body = hit.Actor.GetComponentInParent<AbilityPhysicsBody>();
            if (body == null || body.gameObject == context.Owner)
            {
                return false;
            }

            actor = body.gameObject;
            dedupKey = body;
            return true;
        }

        protected override bool Execute(GameplayEffectSpec spec, bool periodic)
        {
            if (spec.Source == null || eventTag.IsEmpty || spec.Target == null)
            {
                return false;
            }

            AbilityPhysicsBody physicsBody = spec.Target.GetComponent<AbilityPhysicsBody>();
            if (physicsBody == null)
            {
                return false;
            }

            Transform ownerTransform = spec.Source.transform;
            Vector3 center = Targeting.Shape.ResolveCenter(
                ownerTransform, spec.Context.AimPoint);
            Vector3 forceDirection = ResolveDirection(
                ownerTransform, center, physicsBody.transform.position);
            AbilityContext reactionContext = new(
                physicsBody.gameObject,
                spec.Source,
                forceDirection,
                spec.HasHit ? spec.Hit.Point : physicsBody.transform.position);

            AbilitySystem targetSystem = physicsBody.AbilitySystem;
            if (targetSystem == null ||
                !targetSystem.TryHandleGameplayEvent(
                    eventTag, reactionContext, out AbilityDefinition reactionAbility))
            {
                return false;
            }

            Vector3 velocityChange = ResolveVelocityChange(spec.Level, forceDirection);
            return physicsBody.ApplyVelocityChange(
                velocityChange, reactionAbility, application);
        }

        public override bool TryValidate(out string error)
        {
            if (!base.TryValidate(out error))
            {
                return false;
            }

            if (eventTag.IsEmpty)
            {
                error =
                    "Event Tag is empty. The force is delivered by a gameplay " +
                    "event the target reacts to, so without a tag nothing is sent.";
                return false;
            }

            if (horizontalVelocityByLevel == null || horizontalVelocityByLevel.length == 0)
            {
                error = "Horizontal Velocity By Level has no keys, so every level pushes by zero.";
                return false;
            }

            if (verticalVelocityByLevel == null || verticalVelocityByLevel.length == 0)
            {
                error = "Vertical Velocity By Level has no keys, so every level lifts by zero.";
                return false;
            }

            error = null;
            return true;
        }

        public Vector3 ResolveVelocityChange(int level, Vector3 forceDirection)
        {
            int resolvedLevel = Mathf.Clamp(level, 1, 100);
            Vector3 planar = Vector3.ProjectOnPlane(forceDirection, Vector3.up);
            if (planar.sqrMagnitude <= Mathf.Epsilon)
            {
                planar = Vector3.forward;
            }

            float horizontal = Mathf.Max(
                0f,
                horizontalVelocityByLevel?.Evaluate(resolvedLevel) ?? 0f);
            float vertical = verticalVelocityByLevel?.Evaluate(resolvedLevel) ?? 0f;
            return planar.normalized * horizontal + Vector3.up * vertical;
        }

        private Vector3 ResolveDirection(
            Transform owner,
            Vector3 center,
            Vector3 targetPosition)
        {
            Vector3 resolved = direction switch
            {
                PhysicsForceDirection.AwayFromOwner => targetPosition - owner.position,
                PhysicsForceDirection.TowardOwner => owner.position - targetPosition,
                PhysicsForceDirection.AwayFromShapeCenter => targetPosition - center,
                PhysicsForceDirection.TowardShapeCenter => center - targetPosition,
                _ => owner.forward,
            };

            resolved = Vector3.ProjectOnPlane(resolved, Vector3.up);
            return resolved.sqrMagnitude > Mathf.Epsilon
                ? resolved.normalized
                : Vector3.ProjectOnPlane(owner.forward, Vector3.up).normalized;
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            if (eventTag.IsEmpty)
            {
                eventTag = CommonGameplayTags.PhysicsForceEvent;
            }
        }

        internal void ConfigureForTests(
            HitShape hitShape,
            PhysicsForceDirection forceDirection,
            AnimationCurve horizontalCurve,
            AnimationCurve verticalCurve,
            bool restrictTarget = false,
            int targetLimit = 1)
        {
            SetTargetingForTests(
                GameplayEffectTargeting.FromShape(hitShape, targetLimit, restrictTarget));
            direction = forceDirection;
            horizontalVelocityByLevel = horizontalCurve;
            verticalVelocityByLevel = verticalCurve;
            eventTag = CommonGameplayTags.PhysicsForceEvent;
        }

        internal Vector3 ResolveDirectionForTests(
            Transform owner,
            Vector3 center,
            Vector3 targetPosition)
        {
            return ResolveDirection(owner, center, targetPosition);
        }

        internal void SetEventTagForTests(GameplayTag tag)
        {
            eventTag = tag;
        }

        internal void SetBlockedTargetTagsForTests(params GameplayTag[] tags)
        {
            SetTargetingForTests(
                Targeting.WithFilter(Targeting.Filter.WithBlockedTags(tags)));
        }
    }
}
