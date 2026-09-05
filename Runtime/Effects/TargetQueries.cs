using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// Shared target-query helpers for damage effects. Every query is
    /// non-allocating when the caller reuses a static buffer, resolves
    /// receivers from colliders or their parents, and never hits the owner.
    /// </summary>
    public static class TargetQueries
    {
        public static int OverlapReceivers(
            Vector3 center,
            float radius,
            int layerMask,
            Collider[] buffer)
        {
            return Physics.OverlapSphereNonAlloc(
                center,
                radius,
                buffer,
                layerMask == 0 ? Physics.AllLayers : layerMask,
                QueryTriggerInteraction.Collide);
        }

        public static bool TryResolveReceiver(
            Collider targetCollider,
            GameObject owner,
            out IAbilityDamageReceiver receiver,
            out Component receiverComponent)
        {
            receiver = null;
            receiverComponent = null;
            if (targetCollider == null)
            {
                return false;
            }

            receiver = targetCollider.GetComponent<IAbilityDamageReceiver>() ??
                targetCollider.GetComponentInParent<IAbilityDamageReceiver>();
            receiverComponent = receiver as Component;
            return receiver != null &&
                receiverComponent != null &&
                receiver.IsDamageable &&
                receiverComponent.gameObject != owner;
        }

        public static bool MatchesRequestedTarget(
            Transform receiver,
            GameObject requestedTarget)
        {
            if (requestedTarget == null || receiver == null)
            {
                return true;
            }

            Transform requestedTransform = requestedTarget.transform;
            return receiver == requestedTransform ||
                   receiver.IsChildOf(requestedTransform) ||
                   requestedTransform.IsChildOf(receiver);
        }

        /// <summary>
        /// Bonus damage from an attribute (for example, Strength scaling).
        /// Returns zero when the attribute is empty or the owner has no set.
        /// </summary>
        public static int ResolveBonusDamage(
            GameObject owner,
            GameplayAttribute attribute,
            float factor)
        {
            if (owner == null || attribute.IsEmpty || factor <= 0f)
            {
                return 0;
            }

            AttributeSet set = owner.GetComponent<AttributeSet>();
            if (set == null)
            {
                return 0;
            }

            return Mathf.Max(0, Mathf.RoundToInt(set.GetCurrent(attribute) * factor));
        }
    }
}
