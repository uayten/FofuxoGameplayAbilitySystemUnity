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
        [Tooltip("Control-lock applied on hit. Zero interrupts and displaces only.")]
        [SerializeField, Min(0f)] private float stunDurationSeconds;
        [SerializeField] private bool canBeParried = true;
        [SerializeField, Min(1)] private int maximumTargets = 1;
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
            int hitCount = TargetQueries.OverlapReceivers(
                center,
                radius,
                targetLayers.value,
                HitBuffer);
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
                    scaledDamage,
                    context.Owner,
                    targetCollider.ClosestPoint(center),
                    direction,
                    knockback,
                    knockbackDuration,
                    impact,
                    canBeParried,
                    stunDurationSeconds);

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
            horizontalKnockback = Mathf.Max(0f, horizontalKnockback);
            knockbackDuration = Mathf.Max(0f, knockbackDuration);
            stunDurationSeconds = Mathf.Max(0f, stunDurationSeconds);
            maximumTargets = Mathf.Clamp(maximumTargets, 1, HitCapacity);
        }
    }
}
