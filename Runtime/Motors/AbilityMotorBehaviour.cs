using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// Base for the shipped motor adapters. It exists so a consumer can write
    /// its own by overriding one method, and so
    /// <see cref="AbilitySystem"/> finds a motor the same way whatever the
    /// engine underneath.
    ///
    /// The adapters live in their own assembly, outside the core. The core
    /// knows <see cref="IAbilityMotor"/> and nothing else about how an actor
    /// moves, which is what lets a project on a character controller, on a
    /// Rigidbody, or on a motor of its own share the same movement tasks.
    /// </summary>
    [DisallowMultipleComponent]
    public abstract class AbilityMotorBehaviour : MonoBehaviour, IAbilityMotor
    {
        public virtual Vector3 Position => transform.position;

        /// <summary>
        /// Moves the owner by a displacement and returns what was actually
        /// achieved, which is how a blocked push stops short.
        /// </summary>
        public abstract Vector3 Move(Vector3 delta, AbilityMovementCollision collision);
    }
}
