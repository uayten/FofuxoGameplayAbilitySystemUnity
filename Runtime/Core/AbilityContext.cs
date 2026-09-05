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
    }
}
