using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// Pushes the owner along a knockback vector for a duration. It is the hit
    /// reaction half of the knockback split: the attacker effect decides how
    /// hard and how long, and this — running on the target — decides whether the
    /// target actually moves and stops the moment something says otherwise.
    ///
    /// It ticks on the physics step. A knockback issued from <c>Update</c> would
    /// lose whatever the frame rate outran, because a <c>MovePosition</c> target
    /// is replaced by the next one before physics reads it, and a hit would land
    /// shorter at a higher frame rate. Travel that has to be felt belongs on the
    /// fixed step.
    ///
    /// Unlike step displacement the travel is not flattened onto the ground
    /// plane: a launch is authored with a vertical component and has to keep it.
    /// </summary>
    public sealed class ApplyKnockbackTask : AbilityMoveTask
    {
        private readonly Vector3 velocity;
        private readonly GameObject source;

        /// <param name="velocity">World-space knockback velocity, units per second.</param>
        /// <param name="source">
        /// Who dealt the hit. Only used by
        /// <see cref="AbilityMovementDirectionPolicy.Track"/>, which keeps
        /// pushing away from the attacker as it moves; the default snapshot
        /// commits to the vector the hit carried.
        /// </param>
        public ApplyKnockbackTask(
            Vector3 velocity,
            float durationSeconds,
            GameObject source = null,
            AbilityMovementDirectionPolicy directionPolicy =
                AbilityMovementDirectionPolicy.Snapshot)
            : base(velocity.magnitude * Mathf.Max(0f, durationSeconds),
                durationSeconds,
                directionPolicy)
        {
            this.velocity = velocity;
            this.source = source;
            Priority = KnockbackPriority;
        }

        public Vector3 Velocity => velocity;
        public GameObject Source => source;

        public override AbilityTaskTickPhase TickPhase => AbilityTaskTickPhase.FixedUpdate;

        protected override bool TryResolveDirection(out Vector3 resolvedDirection)
        {
            if (DirectionPolicy == AbilityMovementDirectionPolicy.Track &&
                Owner != null &&
                source != null)
            {
                Vector3 away = Owner.transform.position - source.transform.position;
                // The vertical part stays as authored: tracking is about which
                // way the push points on the ground, not about re-aiming a launch.
                away = Vector3.ProjectOnPlane(away, Vector3.up);
                if (away.sqrMagnitude > Mathf.Epsilon)
                {
                    resolvedDirection =
                        away.normalized * new Vector2(velocity.x, velocity.z).magnitude +
                        Vector3.up * velocity.y;
                    return resolvedDirection.sqrMagnitude > Mathf.Epsilon;
                }
            }

            resolvedDirection = velocity;
            return resolvedDirection.sqrMagnitude > Mathf.Epsilon;
        }
    }
}
