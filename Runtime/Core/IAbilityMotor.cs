using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// What a movement task asks the motor to do about the world on its way.
    /// The task never decides this for the motor; it passes the mode down and
    /// the motor honours it, so the same task reads the same in a project that
    /// sweeps and one that does not.
    /// </summary>
    public enum AbilityMovementCollision
    {
        /// <summary>
        /// Root-motion-like travel: the delta is applied as authored and nothing
        /// is swept. The owner world collider and the level are assumed to
        /// already constrain where the body may rest. This is what ability
        /// displacement has always done, and it stays the default.
        /// </summary>
        Unswept,

        /// <summary>
        /// The delta is swept against the world and the body stops short of the
        /// first blocker. Travel through a wall becomes travel up to it.
        /// </summary>
        Swept,

        /// <summary>
        /// The consumer motor decides. The task hands over the delta and assumes
        /// nothing about what happens to it — sliding, stepping, ground snapping
        /// and outright refusal are all the motor prerogative.
        /// </summary>
        MotorAuthority
    }

    /// <summary>
    /// The engine seam for ability-driven movement. The core never touches a
    /// <c>Rigidbody</c>, a <c>CharacterController</c> or a consumer controller
    /// through anything but this: a movement task produces a delta, and whoever
    /// owns the body decides what that delta does to it.
    ///
    /// Implement it on the component that already owns the actor movement — the
    /// adapter direction is game to package, never the reverse — or use one of
    /// the adapters shipped in the <c>Motors</c> assembly.
    /// </summary>
    public interface IAbilityMotor
    {
        /// <summary>Where the motor currently has the body.</summary>
        Vector3 Position { get; }

        /// <summary>
        /// Applies one displacement step.
        /// </summary>
        /// <returns>
        /// What the motor actually moved. A task consumes its own distance
        /// budget regardless, so travel stays deterministic when a wall eats
        /// part of it; the return value is for callers that need to know.
        /// </returns>
        Vector3 Move(Vector3 delta, AbilityMovementCollision collision);
    }
}
