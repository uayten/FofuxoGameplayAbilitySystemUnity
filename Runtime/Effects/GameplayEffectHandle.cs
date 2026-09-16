using System;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// Stable name for one application of an effect. It is issued when an
    /// effect becomes active, never reused, and stays answerable forever: after
    /// the effect it names has expired or been removed, the handle simply
    /// reports that it is no longer active instead of naming something else.
    ///
    /// This is what removal by (attribute, source) could not express — two
    /// applications of the same effect from the same source are two handles.
    /// </summary>
    public readonly struct GameplayEffectHandle : IEquatable<GameplayEffectHandle>
    {
        private static int nextId;

        private readonly int id;

        private GameplayEffectHandle(int id)
        {
            this.id = id;
        }

        /// <summary>
        /// The handle an application that produced no active effect returns:
        /// an instant effect, a blocked one, or one that was ignored.
        /// </summary>
        public static GameplayEffectHandle None => default;

        /// <summary>True for a handle that was ever issued. Says nothing about whether it is still active.</summary>
        public bool IsValid => id != 0;

        public int Id => id;

        internal static GameplayEffectHandle Next()
        {
            nextId++;
            if (nextId == 0)
            {
                nextId = 1;
            }

            return new GameplayEffectHandle(nextId);
        }

        public bool Equals(GameplayEffectHandle other) => id == other.id;

        public override bool Equals(object obj) =>
            obj is GameplayEffectHandle other && Equals(other);

        public override int GetHashCode() => id;

        public override string ToString() =>
            IsValid ? $"GameplayEffect#{id}" : "GameplayEffect#none";

        public static bool operator ==(GameplayEffectHandle left, GameplayEffectHandle right) =>
            left.Equals(right);

        public static bool operator !=(GameplayEffectHandle left, GameplayEffectHandle right) =>
            !left.Equals(right);
    }

    /// <summary>What became of one application.</summary>
    public enum GameplayEffectApplicationOutcome
    {
        /// <summary>Nothing was applied: no target, no definition, or nothing to do.</summary>
        None,
        /// <summary>An instant effect ran. It changed base values and left no active effect.</summary>
        Executed,
        /// <summary>A duration or infinite effect became active, and the handle names it.</summary>
        Applied,
        /// <summary>An existing application absorbed this one: a refresh, a new stack, or an ignore.</summary>
        Stacked,
        /// <summary>The stack was already full. Overflow effects ran, if any were authored.</summary>
        Overflowed,
        /// <summary>Application tags or an active immunity refused it.</summary>
        Blocked,
        /// <summary>The effect itself refused the target, the way a parried hit is refused.</summary>
        Rejected
    }

    /// <summary>
    /// What one application produced: the outcome, and the handle when it
    /// created or reused an active effect.
    /// </summary>
    public readonly struct GameplayEffectApplicationResult
    {
        public GameplayEffectApplicationResult(
            GameplayEffectApplicationOutcome outcome,
            GameplayEffectHandle handle = default)
        {
            Outcome = outcome;
            Handle = handle;
        }

        public GameplayEffectApplicationOutcome Outcome { get; }
        public GameplayEffectHandle Handle { get; }

        /// <summary>
        /// True when the effect reached the target at all: it executed, became
        /// active, stacked, or overflowed a stack that accepted it.
        /// </summary>
        public bool Succeeded =>
            Outcome is GameplayEffectApplicationOutcome.Executed
                or GameplayEffectApplicationOutcome.Applied
                or GameplayEffectApplicationOutcome.Stacked
                or GameplayEffectApplicationOutcome.Overflowed;

        public static GameplayEffectApplicationResult Failed(
            GameplayEffectApplicationOutcome outcome) => new(outcome);
    }
}
