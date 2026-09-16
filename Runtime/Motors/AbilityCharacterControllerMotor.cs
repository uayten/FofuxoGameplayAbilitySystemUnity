using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// Moves a <see cref="CharacterController"/>.
    ///
    /// The controller sweeps and slides by nature, so
    /// <see cref="AbilityMovementCollision.Swept"/> and
    /// <see cref="AbilityMovementCollision.MotorAuthority"/> both go through
    /// <c>Move</c> and let it resolve steps, slopes and blockers.
    /// <see cref="AbilityMovementCollision.Unswept"/> is the one that has to
    /// bypass it: root-motion-like travel is defined as going where it was told,
    /// so it writes the transform directly. Use it only where the level and the
    /// authored distance already guarantee the destination is clear.
    /// </summary>
    [AddComponentMenu("Fofuxo/Ability Character Controller Motor")]
    [RequireComponent(typeof(CharacterController))]
    public sealed class AbilityCharacterControllerMotor : AbilityMotorBehaviour
    {
        private CharacterController controller;

        public CharacterController Controller
        {
            get
            {
                if (controller == null)
                {
                    controller = GetComponent<CharacterController>();
                }

                return controller;
            }
        }

        /// <summary>
        /// Moves the CharacterController and reports the displacement it
        /// actually achieved.
        /// </summary>
        public override Vector3 Move(Vector3 delta, AbilityMovementCollision collision)
        {
            if (delta.sqrMagnitude <= Mathf.Epsilon)
            {
                return Vector3.zero;
            }

            if (collision == AbilityMovementCollision.Unswept)
            {
                transform.position += delta;
                return delta;
            }

            CharacterController resolved = Controller;
            if (resolved == null || !resolved.enabled)
            {
                return Vector3.zero;
            }

            Vector3 before = transform.position;
            resolved.Move(delta);
            return transform.position - before;
        }
    }
}
