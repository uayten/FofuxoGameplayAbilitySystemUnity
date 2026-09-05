using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// Directed box damage for wide swings and cleaves. The box is centered on
    /// an owner-local offset and oriented with the owner. Only receivers
    /// matching the requested target are hit.
    /// </summary>
    [CreateAssetMenu(
        fileName = "BoxDamageEffect",
        menuName = "Fofuxo/Abilities/Effects/Box Damage")]
    public sealed class BoxDamageEffectDefinition : AbilityEffectDefinition
    {
        private const int HitCapacity = 32;
        private static readonly Collider[] HitBuffer = new Collider[HitCapacity];

        [SerializeField] private LayerMask targetLayers;
        [SerializeField] private Vector3 localCenter = new(0f, 1f, 1f);
        [SerializeField] private Vector3 halfExtents = new(1.5f, 1f, 1.5f);
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
            Vector3 center = ownerTransform.TransformPoint(localCenter);
            Quaternion orientation = ownerTransform.rotation;
            int layerMask = targetLayers.value == 0 ? Physics.AllLayers : targetLayers.value;
            int hitCount = Physics.OverlapBoxNonAlloc(
                center,
                halfExtents,
                HitBuffer,
                orientation,
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
                    targetCollider.ClosestPoint(center),
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
            halfExtents = new Vector3(
                Mathf.Max(0.05f, halfExtents.x),
                Mathf.Max(0.05f, halfExtents.y),
                Mathf.Max(0.05f, halfExtents.z));
            damage = Mathf.Max(1, damage);
            maximumTargets = Mathf.Clamp(maximumTargets, 1, HitCapacity);
            horizontalKnockback = Mathf.Max(0f, horizontalKnockback);
            knockbackDuration = Mathf.Max(0f, knockbackDuration);
            scaleFactor = Mathf.Max(0f, scaleFactor);
        }
    }
}
