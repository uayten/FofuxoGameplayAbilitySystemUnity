using System;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// A string-backed label: what an actor is, what an ability requires, what
    /// cancelled one. Authoring data, compared by ordinal string equality.
    /// </summary>
    [Serializable]
    public struct GameplayTag : IEquatable<GameplayTag>
    {
        [SerializeField] private string value;

        public GameplayTag(string value)
        {
            this.value = value?.Trim() ?? string.Empty;
        }

        public string Value => value ?? string.Empty;
        public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

        public bool Equals(GameplayTag other)
        {
            return string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is GameplayTag other && Equals(other);
        }

        public override int GetHashCode()
        {
            return StringComparer.Ordinal.GetHashCode(Value);
        }

        public override string ToString()
        {
            return Value;
        }

        public static bool operator ==(GameplayTag left, GameplayTag right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(GameplayTag left, GameplayTag right)
        {
            return !left.Equals(right);
        }
    }
    /// <summary>
    /// The tags the package itself raises or reads. A game adds its own in the
    /// same namespaces and never edits this class.
    /// </summary>
    public static class CommonGameplayTags
    {
        public static readonly GameplayTag Attacking = new("State.Attacking");
        public static readonly GameplayTag Blocking = new("State.Blocking");
        public static readonly GameplayTag Parrying = new("State.Parrying");
        public static readonly GameplayTag Rolling = new("State.Rolling");
        public static readonly GameplayTag Stunned = new("State.Stunned");
        public static readonly GameplayTag KnockedDown = new("State.KnockedDown");
        public static readonly GameplayTag Dead = new("State.Dead");
        public static readonly GameplayTag ActionLocked = new("State.ActionLocked");
        public static readonly GameplayTag Invulnerable = new("State.Invulnerable");
        public static readonly GameplayTag PhysicsControlled = new("State.PhysicsControlled");
        public static readonly GameplayTag PhysicsForceEvent = new("Event.PhysicsForce");
        /// <summary>
        /// Sent by a damage effect to the target once the damage has landed.
        /// The target's own hit-reaction ability answers it — or refuses to,
        /// which is where i-frames, guard and death are decided.
        /// </summary>
        public static readonly GameplayTag HitReactionEvent = new("Event.HitReaction");
        /// <summary>
        /// The heavy form of the same request, for a hit authored to floor the
        /// target. A separate tag rather than a field on the event, so the
        /// knockdown is its own ability with its own clips, lock and cancel
        /// policy, and a game adds further reactions in the same namespace.
        /// </summary>
        public static readonly GameplayTag KnockdownEvent = new("Event.Knockdown");

        /// <summary>
        /// Cancel requests the package itself raises. A game names its own
        /// reasons in the same `Cancel.` namespace and never edits this class.
        /// </summary>
        public static readonly GameplayTag CancelManual = new("Cancel.Manual");
        public static readonly GameplayTag CancelTargetLost = new("Cancel.TargetLost");
        public static readonly GameplayTag CancelPhysicsForce = new("Cancel.PhysicsForce");
        /// <summary>
        /// A step ran out of timeline with nothing to carry the activation
        /// forward, under a timeout policy that ends the ability as a
        /// cancellation instead of a completion.
        /// </summary>
        public static readonly GameplayTag CancelStepTimeout = new("Cancel.StepTimeout");
        /// <summary>
        /// Another activation took this one's place under a mutual-exclusion
        /// policy that cancels instead of blocking — Cancel Same Group or
        /// Cancel Any Active. The group is not part of the name because the
        /// second policy has none: what the ended ability learns is that it was
        /// superseded, not by what rule.
        /// </summary>
        public static readonly GameplayTag CancelSuperseded =
            new("Cancel.Superseded");
        /// <summary>
        /// The activation threw while executing: an ability hook, a gameplay
        /// effect, a damage receiver or an event handler raised an exception,
        /// and the system tore the activation down rather than leaving it half
        /// advanced. The exception is logged before the cancellation is raised.
        ///
        /// A consumer that retries, reports a bug, or falls back to a safe
        /// state reads this and never <see cref="CancelManual"/>: a deliberate
        /// cancel means the game asked, and this one means the game broke.
        /// </summary>
        public static readonly GameplayTag CancelFailed = new("Cancel.Failed");
        /// <summary>
        /// A periodic cost came due and could not be paid — the stamina a
        /// sprint runs on ran out — so the activation ended whatever its cancel
        /// policy says.
        /// </summary>
        public static readonly GameplayTag CancelInsufficientCost = new("Cancel.InsufficientCost");
        /// <summary>
        /// The ability asked to commit and could not: the cost could no longer
        /// be paid or the cooldown had started in the meantime.
        /// </summary>
        public static readonly GameplayTag CancelCommitFailed = new("Cancel.CommitFailed");
        /// <summary>
        /// The owner is going away — the component was disabled, the
        /// GameObject destroyed, or the scene unloaded — so every activation
        /// ends whatever its cancel policy says.
        ///
        /// It is separate from <see cref="CancelManual"/> because a handler
        /// must be able to tell "the player rolled out of the swing" from
        /// "there is no longer an actor here": the second one must not start
        /// anything, queue anything, or touch the owner.
        /// </summary>
        public static readonly GameplayTag CancelOwnerTeardown =
            new("Cancel.OwnerTeardown");

        /// <summary>
        /// The owner may not move under its own input. Authored as a step tag
        /// window so it ends on an exact frame, well before the animation does.
        /// </summary>
        public static readonly GameplayTag MovementLocked = new("State.MovementLocked");
    }
}
