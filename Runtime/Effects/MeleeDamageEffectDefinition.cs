using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    [CreateAssetMenu(
        fileName = "MeleeDamageEffect",
        menuName = "Fofuxo/Abilities/Effects/Melee Damage")]
    public sealed class MeleeDamageEffectDefinition : AbilityEffectDefinition
    {
        private const int HitCapacity = 32;
        private static readonly Collider[] HitBuffer = new Collider[HitCapacity];

        [SerializeField] private LayerMask targetLayers;
        [SerializeField] private Vector3 localCenter = new(0f, 1f, 1.05f);
        [SerializeField, Min(0.05f)] private float radius = 1.05f;
        [SerializeField, Min(1)] private int damage = 1;
        [SerializeField, Min(0f)] private float horizontalKnockback;
        [SerializeField] private float verticalKnockback;
        [SerializeField, Min(0f)] private float knockbackDuration;
        [SerializeField] private AbilityImpact impact = AbilityImpact.Light;
        [SerializeField] private bool canBeParried = true;
        [SerializeField, Min(1)] private int maximumTargets = 1;

        public override void Apply(AbilityEffectContext context)
        {
            if (context.Owner == null)
            {
                return;
            }

            Transform ownerTransform = context.Owner.transform;
            Vector3 center = ownerTransform.TransformPoint(localCenter);
            int layerMask = targetLayers.value == 0 ? Physics.AllLayers : targetLayers.value;
            int hitCount = Physics.OverlapSphereNonAlloc(
                center,
                radius,
                HitBuffer,
                layerMask,
                QueryTriggerInteraction.Collide);
            int acceptedTargets = 0;

            for (int i = 0; i < hitCount && acceptedTargets < maximumTargets; i++)
            {
                Collider targetCollider = HitBuffer[i];
                if (targetCollider == null)
                {
                    continue;
                }

                IAbilityDamageReceiver receiver =
                    targetCollider.GetComponent<IAbilityDamageReceiver>() ??
                    targetCollider.GetComponentInParent<IAbilityDamageReceiver>();
                Component receiverComponent = receiver as Component;
                if (receiver == null ||
                    receiverComponent == null ||
                    !receiver.IsDamageable ||
                    receiverComponent.gameObject == context.Owner ||
                    !MatchesRequestedTarget(receiverComponent.transform, context.Target) ||
                    !context.Instance.TryRegisterHit(context.TriggerIndex, receiverComponent))
                {
                    continue;
                }

                Vector3 direction = receiverComponent.transform.position - ownerTransform.position;
                Vector3 planarDirection = Vector3.ProjectOnPlane(direction, Vector3.up);
                if (planarDirection.sqrMagnitude <= Mathf.Epsilon)
                {
                    planarDirection = ownerTransform.forward;
                }

                planarDirection.Normalize();
                Vector3 knockback =
                    planarDirection * horizontalKnockback +
                    Vector3.up * verticalKnockback;
                AbilityHitInfo hitInfo = new(
                    damage,
                    context.Owner,
                    targetCollider.ClosestPoint(center),
                    direction,
                    knockback,
                    knockbackDuration,
                    impact,
                    canBeParried);

                if (receiver.TryReceiveDamage(hitInfo))
                {
                    acceptedTargets++;
                }
            }
        }

        private static bool MatchesRequestedTarget(Transform receiver, GameObject requestedTarget)
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

        private void OnValidate()
        {
            radius = Mathf.Max(0.05f, radius);
            damage = Mathf.Max(1, damage);
            horizontalKnockback = Mathf.Max(0f, horizontalKnockback);
            knockbackDuration = Mathf.Max(0f, knockbackDuration);
            maximumTargets = Mathf.Clamp(maximumTargets, 1, HitCapacity);
        }
    }
}
