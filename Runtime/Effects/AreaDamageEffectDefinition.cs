using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// Sphere damage centered on the ability aim point (for targeted slams
    /// and shockwaves) or on an owner-local offset (for self-centered bursts).
    /// Unlike melee, every damageable receiver in radius is hit; there is no
    /// requested-target filter. Damage optionally falls off linearly from the
    /// center, and knockback pushes radially away from it.
    /// </summary>
    [CreateAssetMenu(
        fileName = "AreaDamageEffect",
        menuName = "Fofuxo/Abilities/Effects/Area Damage")]
    public sealed class AreaDamageEffectDefinition : AbilityEffectDefinition
    {
        public enum AreaCenter
        {
            AbilityAimPoint,
            OwnerLocalOffset
        }

        private const int HitCapacity = 32;
        private static readonly Collider[] HitBuffer = new Collider[HitCapacity];

        [SerializeField] private AreaCenter centerMode = AreaCenter.AbilityAimPoint;
        [SerializeField] private Vector3 localCenter = new(0f, 1f, 0f);
        [SerializeField, Min(0.5f)] private float radius = 3f;
        [SerializeField] private LayerMask targetLayers;
        [SerializeField, Min(1)] private int damage = 10;
        [SerializeField] private bool linearFalloff = true;
        [SerializeField, Min(1)] private int maximumTargets = 8;
        [SerializeField, Min(0f)] private float radialKnockback = 4f;
        [SerializeField] private float verticalKnockback = 2f;
        [SerializeField, Min(0f)] private float knockbackDuration = 0.2f;
        [SerializeField] private AbilityImpact impact = AbilityImpact.Heavy;
        [SerializeField] private bool canBeParried;
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
            Vector3 center = centerMode == AreaCenter.AbilityAimPoint
                ? context.AbilityContext.AimPoint
                : ownerTransform.TransformPoint(localCenter);
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
                    !context.Instance.TryRegisterHit(context.TriggerIndex, receiverComponent))
                {
                    continue;
                }

                Collider targetCollider = HitBuffer[i];
                Vector3 toReceiver =
                    receiverComponent.transform.position - center;
                float distance = toReceiver.magnitude;
                Vector3 planarDirection = Vector3.ProjectOnPlane(toReceiver, Vector3.up);
                if (planarDirection.sqrMagnitude <= Mathf.Epsilon)
                {
                    planarDirection = ownerTransform.forward;
                }

                planarDirection.Normalize();
                int finalDamage = linearFalloff
                    ? Mathf.Max(1, Mathf.RoundToInt(scaledDamage * (1f - Mathf.Clamp01(distance / radius))))
                    : scaledDamage;
                Vector3 knockback =
                    planarDirection * radialKnockback +
                    Vector3.up * verticalKnockback;
                AbilityHitInfo hitInfo = new(
                    finalDamage,
                    context.Owner,
                    targetCollider.ClosestPoint(center),
                    toReceiver,
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
            radius = Mathf.Max(0.5f, radius);
            damage = Mathf.Max(1, damage);
            maximumTargets = Mathf.Clamp(maximumTargets, 1, HitCapacity);
            radialKnockback = Mathf.Max(0f, radialKnockback);
            knockbackDuration = Mathf.Max(0f, knockbackDuration);
        }
    }
}
