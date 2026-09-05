using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    public enum AbilityImpact
    {
        Light,
        Heavy,
        Knockdown
    }

    /// <summary>
    /// Immutable data for a hit dealt by an ability effect. Besides damage, it
    /// carries a world-space knockback vector so each target can decide how to
    /// react to it.
    /// </summary>
    public readonly struct AbilityHitInfo
    {
        public AbilityHitInfo(
            int amount,
            GameObject source,
            Vector3 hitPoint,
            Vector3 direction,
            Vector3 knockback = default,
            float knockbackDuration = 0f,
            AbilityImpact impact = AbilityImpact.Light,
            bool canBeParried = true,
            float stunDuration = 0f)
        {
            Amount = Mathf.Max(0, amount);
            Source = source;
            HitPoint = hitPoint;
            Direction = direction.sqrMagnitude > Mathf.Epsilon
                ? direction.normalized
                : Vector3.zero;
            Knockback = knockback;
            KnockbackDuration = Mathf.Max(0f, knockbackDuration);
            Impact = impact;
            CanBeParried = canBeParried;
            StunDuration = Mathf.Max(0f, stunDuration);
        }

        public int Amount { get; }
        public GameObject Source { get; }
        public Vector3 HitPoint { get; }
        public Vector3 Direction { get; }

        /// <summary>
        /// World-space knockback velocity, in units per second.
        /// </summary>
        public Vector3 Knockback { get; }

        /// <summary>
        /// Duration, in seconds, for which the target should apply knockback.
        /// </summary>
        public float KnockbackDuration { get; }
        public AbilityImpact Impact { get; }
        public bool CanBeParried { get; }

        /// <summary>
        /// Control-lock duration, in seconds, the target should apply. Zero
        /// means interrupt and displace only. Authored per attack.
        /// </summary>
        public float StunDuration { get; }
    }

    /// <summary>
    /// Contract an ability effect uses to deal damage without knowing the
    /// game's health implementation. Implement it on the game's own health or
    /// damage-receiver component (it must also be a <see cref="Component"/> so
    /// effects can resolve it from a hit collider).
    /// </summary>
    public interface IAbilityDamageReceiver
    {
        bool IsDamageable { get; }

        /// <returns>True only when the damage was accepted and applied.</returns>
        bool TryReceiveDamage(AbilityHitInfo hit);
    }
}
