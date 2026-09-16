using System;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// The seam for a project that already has a motor of its own. Assign
    /// <see cref="MoveHandler"/> from that motor and it receives every ability
    /// delta, under full <see cref="AbilityMovementCollision.MotorAuthority"/>:
    /// how the delta becomes movement — sliding, stepping, ground snapping,
    /// refusing outright — stays entirely on the consumer side.
    ///
    /// A consumer whose controller can implement <see cref="IAbilityMotor"/>
    /// directly should do that instead; this exists for the case where it
    /// cannot, which is common when the motor is third-party or sealed.
    /// </summary>
    [AddComponentMenu("Fofuxo/Ability Delegate Motor")]
    public sealed class AbilityDelegateMotor : AbilityMotorBehaviour
    {
        /// <summary>
        /// Receives the delta and the requested collision mode, and returns what
        /// it actually moved. Unassigned means the actor refuses ability travel,
        /// which is a valid answer and not an error.
        /// </summary>
        public Func<Vector3, AbilityMovementCollision, Vector3> MoveHandler { get; set; }

        /// <summary>
        /// Where the motor reports the body, when the transform is not it —
        /// an interpolated proxy, for instance. Optional.
        /// </summary>
        public Func<Vector3> PositionSource { get; set; }

        public override Vector3 Position =>
            PositionSource != null ? PositionSource.Invoke() : transform.position;

        public override Vector3 Move(Vector3 delta, AbilityMovementCollision collision)
        {
            if (MoveHandler == null || delta.sqrMagnitude <= Mathf.Epsilon)
            {
                return Vector3.zero;
            }

            return MoveHandler.Invoke(delta, collision);
        }
    }
}
