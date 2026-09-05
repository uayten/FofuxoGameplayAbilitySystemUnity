using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    public readonly struct AbilityContext
    {
        public AbilityContext(
            GameObject owner,
            GameObject target,
            Vector3 direction,
            Vector3 aimPoint)
        {
            Owner = owner;
            Target = target;
            Direction = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
            AimPoint = aimPoint;
        }

        public GameObject Owner { get; }
        public GameObject Target { get; }
        public Vector3 Direction { get; }
        public Vector3 AimPoint { get; }

        public static AbilityContext FromTarget(GameObject owner, GameObject target)
        {
            Vector3 direction = owner != null && target != null
                ? target.transform.position - owner.transform.position
                : owner != null
                    ? owner.transform.forward
                    : Vector3.forward;
            Vector3 aimPoint = target != null
                ? target.transform.position
                : owner != null
                    ? owner.transform.position + direction
                    : direction;
            return new AbilityContext(owner, target, direction, aimPoint);
        }

        /// <summary>
        /// Builds a context for directional, targetless abilities such as rolls,
        /// dashes, or lunges. The supplied direction is projected onto the ground
        /// plane; when it is empty, the owner's forward is used instead so the
        /// context always carries a usable facing.
        /// </summary>
        public static AbilityContext FromDirection(
            GameObject owner,
            GameObject target,
            Vector3 direction)
        {
            Vector3 planarDirection = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (planarDirection.sqrMagnitude <= Mathf.Epsilon)
            {
                planarDirection = owner != null ? owner.transform.forward : Vector3.forward;
            }

            Vector3 aimPoint = owner != null
                ? owner.transform.position + planarDirection.normalized
                : planarDirection.normalized;
            return new AbilityContext(owner, target, planarDirection, aimPoint);
        }
    }
}
