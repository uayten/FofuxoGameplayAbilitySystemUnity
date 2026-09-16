using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// Shared target-query helpers. Every query is non-allocating when the
    /// caller reuses a static buffer, and resolves colliders to the actors they
    /// belong to.
    ///
    /// Who is accepted — owner exclusion, alive state, tags, line of sight — is
    /// <see cref="AbilityTargetFilter"/>'s job, not this one's. This resolves,
    /// it does not judge.
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

        /// <summary>
        /// The actor a collider belongs to: the nearest parent that holds
        /// gameplay state — an <see cref="AbilitySystem"/>, an
        /// <see cref="AttributeSet"/> or a <see cref="GameplayEffectContainer"/>
        /// — else the Rigidbody the collider is attached to, else the collider's
        /// own GameObject. A hitbox child resolves to its character; scenery
        /// resolves to itself, and each effect decides whether it can act on
        /// what it was handed.
        /// </summary>
        public static GameObject ResolveActor(Collider targetCollider)
        {
            if (targetCollider == null)
            {
                return null;
            }

            AbilitySystem system = targetCollider.GetComponentInParent<AbilitySystem>();
            if (system != null)
            {
                return system.gameObject;
            }

            AttributeSet attributes = targetCollider.GetComponentInParent<AttributeSet>();
            if (attributes != null)
            {
                return attributes.gameObject;
            }

            GameplayEffectContainer container =
                targetCollider.GetComponentInParent<GameplayEffectContainer>();
            if (container != null)
            {
                return container.gameObject;
            }

            return targetCollider.attachedRigidbody != null
                ? targetCollider.attachedRigidbody.gameObject
                : targetCollider.gameObject;
        }

        /// <summary>
        /// Whether a resolved actor is an actor at all — something that holds
        /// gameplay state — as opposed to the scenery a collider on the layer
        /// turned out to be. The filter skips scenery by default.
        /// </summary>
        public static bool IsActor(GameObject actor)
        {
            return actor != null &&
                   (actor.GetComponentInParent<AbilitySystem>() != null ||
                    actor.GetComponentInParent<AttributeSet>() != null ||
                    actor.GetComponentInParent<GameplayEffectContainer>() != null);
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
