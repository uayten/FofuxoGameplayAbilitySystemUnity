using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// Travels a fixed distance over a fixed duration in one direction. This is
    /// the ability-timeline displacement — a roll, a dash, a lunge — turned into
    /// a task: the authored step still says how far, how long and which way, and
    /// this is what carries it out.
    ///
    /// Travel is planar by default, because a step displacement that inherited a
    /// vertical component from a downhill aim would climb, and the ability
    /// timeline is not a jump.
    /// </summary>
    public sealed class MoveByDistanceTask : AbilityMoveTask
    {
        private readonly AbilityDisplacementDirection directionMode;
        private readonly Vector3 explicitDirection;
        private readonly bool useExplicitDirection;
        private readonly bool projectOnGroundPlane;

        /// <summary>
        /// Travels along a direction resolved from the activation context, the
        /// way an authored step does. Bind it to the step timeline with
        /// <see cref="AbilityMoveTask.SetFrameWindow"/>.
        /// </summary>
        public MoveByDistanceTask(
            AbilityDisplacementDirection directionMode,
            float distance,
            float durationSeconds,
            AbilityMovementDirectionPolicy directionPolicy =
                AbilityMovementDirectionPolicy.Snapshot)
            : base(distance, durationSeconds, directionPolicy)
        {
            this.directionMode = directionMode;
            projectOnGroundPlane = true;
        }

        /// <summary>
        /// Travels along a caller-supplied direction. Snapshot only: an explicit
        /// vector has nothing live to track.
        /// </summary>
        public MoveByDistanceTask(
            Vector3 direction,
            float distance,
            float durationSeconds,
            bool projectOnGroundPlane = true)
            : base(distance, durationSeconds, AbilityMovementDirectionPolicy.Snapshot)
        {
            explicitDirection = direction;
            useExplicitDirection = true;
            this.projectOnGroundPlane = projectOnGroundPlane;
        }

        protected override bool TryResolveDirection(out Vector3 resolvedDirection)
        {
            if (useExplicitDirection)
            {
                resolvedDirection = projectOnGroundPlane
                    ? Vector3.ProjectOnPlane(explicitDirection, Vector3.up)
                    : explicitDirection;
                return resolvedDirection.sqrMagnitude > Mathf.Epsilon;
            }

            AbilityContext context = Instance != null
                ? Instance.Context
                : AbilityContext.FromDirection(Owner, null, Vector3.forward);
            resolvedDirection = AbilityDisplacement.ResolveDirection(directionMode, context);
            return resolvedDirection.sqrMagnitude > Mathf.Epsilon;
        }
    }
}
