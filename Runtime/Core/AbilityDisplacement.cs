using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// Where an ability-owned displacement reads its travel direction.
    /// The direction is resolved once at activation (snapshot semantics):
    /// the system never re-homes toward a moving target mid-flight.
    /// </summary>
    public enum AbilityDisplacementDirection
    {
        Context,
        OwnerForward,
        TowardTarget,
        AwayFromTarget
    }

    /// <summary>
    /// Pure helpers for ability-owned displacement. Displacement is kinematic
    /// travel owned by the ability timeline (meters over a frame window),
    /// applied with Rigidbody.MovePosition. It never touches velocity:
    /// the owner's motor owns velocity, the ability only adds travel.
    /// Collision is intentionally not swept; like root motion, displacement
    /// assumes the owner's world collider and the level already constrain
    /// where the body may rest.
    /// </summary>
    public static class AbilityDisplacement
    {
        private const float DirectionEpsilon = 0.0001f;

        public static Vector3 ResolveDirection(
            AbilityDisplacementDirection mode,
            AbilityContext context)
        {
            Vector3 direction = mode switch
            {
                AbilityDisplacementDirection.OwnerForward => GetOwnerForward(context),
                AbilityDisplacementDirection.TowardTarget => GetToTarget(context),
                AbilityDisplacementDirection.AwayFromTarget => -GetToTarget(context),
                _ => context.Direction,
            };

            direction = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (direction.sqrMagnitude <= DirectionEpsilon * DirectionEpsilon)
            {
                direction = Vector3.ProjectOnPlane(GetOwnerForward(context), Vector3.up);
            }

            if (direction.sqrMagnitude <= DirectionEpsilon * DirectionEpsilon)
            {
                return Vector3.forward;
            }

            return direction.normalized;
        }

        /// <summary>
        /// Seconds covered by the 1-based frame window [startFrame, endFrame].
        /// Frame F is reached at (F - 1) / frameRate seconds, so the window
        /// duration is (endFrame - startFrame) / frameRate.
        /// </summary>
        public static float WindowDurationSeconds(
            int startFrame,
            int endFrame,
            float frameRate)
        {
            if (endFrame <= startFrame || frameRate <= Mathf.Epsilon)
            {
                return 0f;
            }

            return (endFrame - startFrame) / frameRate;
        }

        private static Vector3 GetOwnerForward(AbilityContext context)
        {
            return context.Owner != null
                ? context.Owner.transform.forward
                : Vector3.forward;
        }

        private static Vector3 GetToTarget(AbilityContext context)
        {
            if (context.Owner == null || context.Target == null)
            {
                return GetOwnerForward(context);
            }

            return context.Target.transform.position - context.Owner.transform.position;
        }
    }
}
