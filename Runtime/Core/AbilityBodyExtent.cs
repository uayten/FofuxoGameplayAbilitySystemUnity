using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// How far an actor own body reaches along a direction, measured from its
    /// colliders rather than assumed. Without it an authored gap would be
    /// measured from the actor origin, and a wide character would stop with its
    /// body still half a metre short.
    ///
    /// Shared by the target-assist prelude and by
    /// <see cref="MoveTowardTargetTask"/>, which is the point: the approach the
    /// assist plans and the approach the task performs measure the same way.
    /// </summary>
    public static class AbilityBodyExtent
    {
        private const float ProbeDistance = 100f;

        /// <summary>
        /// Measures against a collider set the caller already cached. Solid
        /// colliders only: a trigger describes a gameplay volume, not the body.
        /// </summary>
        public static float Resolve(
            Vector3 origin, Collider[] colliders, Vector3 direction)
        {
            if (colliders == null || colliders.Length == 0)
            {
                return 0f;
            }

            Vector3 planar = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (planar.sqrMagnitude <= Mathf.Epsilon)
            {
                planar = direction;
            }

            if (planar.sqrMagnitude <= Mathf.Epsilon)
            {
                return 0f;
            }

            planar.Normalize();
            Vector3 probe = origin + planar * ProbeDistance;
            float extent = 0f;

            for (int i = 0; i < colliders.Length; i++)
            {
                Collider candidate = colliders[i];
                if (candidate == null || candidate.isTrigger)
                {
                    continue;
                }

                float reach = Vector3.Dot(candidate.ClosestPoint(probe) - origin, planar);
                if (reach > extent)
                {
                    extent = reach;
                }
            }

            return extent;
        }

        /// <summary>
        /// Measures an actor by looking its colliders up. Callers on a hot path
        /// cache the array and use the overload above instead.
        /// </summary>
        public static float Resolve(GameObject actor, Vector3 direction)
        {
            if (actor == null)
            {
                return 0f;
            }

            return Resolve(
                actor.transform.position,
                actor.GetComponentsInChildren<Collider>(true),
                direction);
        }
    }
}
