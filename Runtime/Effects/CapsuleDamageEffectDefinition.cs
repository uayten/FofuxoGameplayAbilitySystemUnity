using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// Capsule damage along the owner's forward for lunges and charges. The
    /// capsule runs from an owner-local start to an owner-local end point.
    /// Only receivers matching the requested target are hit.
    /// </summary>
    [CreateAssetMenu(
        fileName = "CapsuleDamageEffect",
        menuName = "Fofuxo/Abilities/Effects/Capsule Damage")]
    public sealed class CapsuleDamageEffectDefinition : AbilityEffectDefinition
    {
        private const int HitCapacity = 32;
        private static readonly Collider[] HitBuffer = new Collider[HitCapacity];

        [SerializeField] private LayerMask targetLayers;
        [SerializeField] private Vector3 localStart = new(0f, 1f, 0f);
        [SerializeField] private Vector3 localEnd = new(0f, 1f, 3f);
        [SerializeField, Min(0.05f)] private float radius = 1f;
        [SerializeField, Min(1)] private int damage = 1;
        [SerializeField, Min(0f)] private float horizontalKnockback;
        [SerializeField] private float verticalKnockback;
        [SerializeField, Min(0f)] private float knockbackDuration;
        [SerializeField] private AbilityImpact impact = AbilityImpact.Light;
        [SerializeField] private bool canBeParried = true;
        [SerializeField, Min(1)] private int maximumTargets = 3;
        [Header("Attribute Scaling")]
        [SerializeField] private GameplayAttribute scaleAttribute;
        [SerializeField, Min(0f)] private float scaleFactor;

        public override void Apply(AbilityEffectContext context)
        {
            if (context.Owner == null)
            {
                return;
            }

            Transform ownerTransform = context.Owner.transform;
            Vector3 start = ownerTransform.TransformPoint(localStart);
            Vector3 end = ownerTransform.TransformPoint(localEnd);
            int layerMask = targetLayers.value == 0 ? Physics.AllLayers : targetLayers.value;
            int hitCount = Physics.OverlapCapsuleNonAlloc(
                start,
                end,
                radius,
                HitBuffer,
                layerMask,
                QueryTriggerInteraction.Collide);
            int scaledDamage =
                damage + TargetQueries.ResolveBonusDamage(
                    context.Owner, scaleAttribute, scaleFactor);
            int acceptedTargets = 0;

            for (int i = 0; i < hitCount && acceptedTargets < maximumTargets; i++)
            {
                if (!TargetQueries.TryResolveReceiver(
                        HitBuffer[i],
                        context.Owner,
                        out IAbilityDamageReceiver receiver,
                        out Component receiverComponent) ||
                    !TargetQueries.MatchesRequestedTarget(
                        receiverComponent.transform, context.Target) ||
                    !context.Instance.TryRegisterHit(context.TriggerIndex, receiverComponent))
                {
                    continue;
                }

                Collider targetCollider = HitBuffer[i];
                Vector3 planarDirection = Vector3.ProjectOnPlane(
                    ownerTransform.forward, Vector3.up);
                if (planarDirection.sqrMagnitude <= Mathf.Epsilon)
                {
                    planarDirection = ownerTransform.forward;
                }

                planarDirection.Normalize();
                Vector3 knockback =
                    planarDirection * horizontalKnockback +
                    Vector3.up * verticalKnockback;
                AbilityHitInfo hitInfo = new(
                    scaledDamage,
                    context.Owner,
                    targetCollider.ClosestPoint(start),
                    ownerTransform.forward,
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

        private void OnValidate()
        {
            radius = Mathf.Max(0.05f, radius);
            damage = Mathf.Max(1, damage);
            maximumTargets = Mathf.Clamp(maximumTargets, 1, HitCapacity);
            horizontalKnockback = Mathf.Max(0f, horizontalKnockback);
            knockbackDuration = Mathf.Max(0f, knockbackDuration);
            scaleFactor = Mathf.Max(0f, scaleFactor);
        }
    }
}
