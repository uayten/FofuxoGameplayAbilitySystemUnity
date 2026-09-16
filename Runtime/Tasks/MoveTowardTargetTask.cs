using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// Closes the gap to a target and stops at an authored distance from its
    /// body. This is the target-assist approach as a task, and it measures the
    /// same way the assist prelude always did: the distance to the target
    /// surface, minus the owner own extent along the approach, minus the
    /// authored stopping gap, so zero gap means the two bodies end up touching.
    ///
    /// The direction policy is the difference between the two approaches an
    /// attack can want. <see cref="AbilityMovementDirectionPolicy.Snapshot"/>
    /// commits to where the target was when the step started — what the assist
    /// has always done, and what keeps a swing landing where it was aimed.
    /// <see cref="AbilityMovementDirectionPolicy.Track"/> re-reads the target
    /// every tick and re-measures the gap with it, which follows an enemy that
    /// steps aside.
    /// </summary>
    public sealed class MoveTowardTargetTask : AbilityMoveTask
    {
        private readonly GameObject target;
        private readonly float stoppingGap;
        private readonly bool measureFromBodies;
        private Collider[] ownerColliders;

        /// <param name="target">
        /// What to approach. Null falls back to the activation target, so a task
        /// started from a step follows whatever the prelude committed to.
        /// </param>
        /// <param name="distance">
        /// The planned travel. Pass a negative value to have the task measure it
        /// from the two bodies itself, which is what a caller with no prelude
        /// wants.
        /// </param>
        public MoveTowardTargetTask(
            GameObject target,
            float distance,
            float durationSeconds,
            float stoppingGap = 0f,
            AbilityMovementDirectionPolicy directionPolicy =
                AbilityMovementDirectionPolicy.Snapshot)
            : base(Mathf.Max(0f, distance), durationSeconds, directionPolicy)
        {
            this.target = target;
            this.stoppingGap = Mathf.Max(0f, stoppingGap);
            measureFromBodies = distance < 0f;
            Priority = ApproachPriority;
        }

        public GameObject Target => target != null
            ? target
            : Instance?.Context.Target;

        public float StoppingGap => stoppingGap;

        protected override void OnStart()
        {
            if (measureFromBodies && TryMeasureGap(out float gap))
            {
                // Re-seeding through the base needs a running task, so the
                // measurement happens before the base decides the travel is
                // empty and succeeds on the spot.
                SetMeasuredDistance(gap);
            }

            base.OnStart();
        }

        protected override bool TryResolveDirection(out Vector3 resolvedDirection)
        {
            resolvedDirection = Vector3.zero;
            GameObject resolved = Target;
            if (Owner == null || resolved == null)
            {
                return false;
            }

            resolvedDirection = Vector3.ProjectOnPlane(
                resolved.transform.position - Owner.transform.position,
                Vector3.up);
            return resolvedDirection.sqrMagnitude > Mathf.Epsilon;
        }

        /// <summary>
        /// A tracking approach re-measures the gap as the target moves; a
        /// snapshot one keeps the distance the prelude planned, so it lands
        /// where it was aimed even if the enemy walked off.
        /// </summary>
        protected override bool TryUpdateRemainingDistance(out float distance)
        {
            distance = RemainingDistance;
            if (DirectionPolicy != AbilityMovementDirectionPolicy.Track)
            {
                return false;
            }

            if (!TryMeasureGap(out float gap))
            {
                return false;
            }

            distance = gap;
            return true;
        }

        private bool TryMeasureGap(out float gap)
        {
            gap = 0f;
            GameObject resolved = Target;
            if (Owner == null || resolved == null)
            {
                return false;
            }

            Vector3 origin = Owner.transform.position;
            Vector3 planar = Vector3.ProjectOnPlane(
                resolved.transform.position - origin, Vector3.up);
            if (planar.sqrMagnitude <= Mathf.Epsilon)
            {
                return false;
            }

            Vector3 direction = planar.normalized;
            Collider targetCollider = resolved.GetComponentInChildren<Collider>();
            float surfaceDistance = targetCollider != null
                ? Vector3.Dot(targetCollider.ClosestPoint(origin) - origin, direction)
                : planar.magnitude;

            ownerColliders ??= Owner.GetComponentsInChildren<Collider>(true);
            float ownerExtent = AbilityBodyExtent.Resolve(origin, ownerColliders, direction);
            gap = Mathf.Max(0f, surfaceDistance - ownerExtent - stoppingGap);
            return true;
        }
    }
}
