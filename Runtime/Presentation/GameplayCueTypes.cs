using System;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// Which moment of a cue's life a presenter is being told about. A burst
    /// cue only ever executes; a persistent one is added, refreshed while it
    /// lives, and removed — under one <see cref="GameplayCueHandle"/> from
    /// start to end, which is what lets a pooled effect be returned.
    /// </summary>
    public enum GameplayCueEvent
    {
        /// <summary>Fire once, now. An impact, a tell, a footstep.</summary>
        Execute,
        /// <summary>A persistent cue began. Start the loop, keep it under the handle.</summary>
        Add,
        /// <summary>
        /// A persistent cue is live: its parameters changed — a stack was added
        /// — or a presenter registered after it began and is being caught up.
        /// Present the steady state; never replay the start.
        /// </summary>
        WhileActive,
        /// <summary>The persistent cue ended. Stop what the handle names.</summary>
        Remove
    }

    /// <summary>
    /// What happened to the thing the cue presents. The same authored cue tag
    /// can be kept, replaced or suppressed per outcome, so a swing that met a
    /// parry sounds like a parry and a swing that met nothing sounds like air.
    /// </summary>
    public enum GameplayCueOutcome
    {
        /// <summary>The effect or the step did what it set out to do.</summary>
        Landed,
        /// <summary>A step cue fired on a frame where no effect of the step had hit anything.</summary>
        Missed,
        /// <summary>The application was refused by tags the target holds — guard, i-frames, death.</summary>
        Blocked,
        /// <summary>The application was refused by an active immunity.</summary>
        Immune,
        /// <summary>The application was refused by a target that was parrying.</summary>
        Parried
    }

    /// <summary>
    /// Names one raised cue for as long as it lives. Every cue gets one; a
    /// persistent cue keeps it from <see cref="GameplayCueEvent.Add"/> to
    /// <see cref="GameplayCueEvent.Remove"/>, so a presenter can pool, look up
    /// and return whatever it spawned by this key alone.
    /// </summary>
    public readonly struct GameplayCueHandle : IEquatable<GameplayCueHandle>
    {
        private static int nextId;

        private readonly int id;

        private GameplayCueHandle(int id)
        {
            this.id = id;
        }

        public static readonly GameplayCueHandle None = default;

        public int Id => id;
        public bool IsValid => id != 0;

        internal static GameplayCueHandle Next()
        {
            nextId++;
            if (nextId == 0)
            {
                nextId = 1;
            }

            return new GameplayCueHandle(nextId);
        }

        public bool Equals(GameplayCueHandle other) => id == other.id;
        public override bool Equals(object obj) => obj is GameplayCueHandle other && Equals(other);
        public override int GetHashCode() => id;
        public static bool operator ==(GameplayCueHandle left, GameplayCueHandle right) => left.id == right.id;
        public static bool operator !=(GameplayCueHandle left, GameplayCueHandle right) => left.id != right.id;
        public override string ToString() => IsValid ? $"cue#{id}" : "cue#none";
    }

    /// <summary>
    /// The cue an effect raises for one outcome instead of its own — a parry
    /// clang instead of the hit thud — or, with an empty tag, nothing at all.
    /// </summary>
    [Serializable]
    public struct GameplayCueReplacement
    {
        [Tooltip("The outcome this entry applies to.")]
        [SerializeField] private GameplayCueOutcome outcome;
        [Tooltip("Cue raised for that outcome. Empty suppresses the cue for it.")]
        [SerializeField] private GameplayTag cue;

        public GameplayCueReplacement(GameplayCueOutcome outcome, GameplayTag cue)
        {
            this.outcome = outcome;
            this.cue = cue;
        }

        public GameplayCueOutcome Outcome => outcome;
        public GameplayTag Cue => cue;
    }

    /// <summary>
    /// Something that turns cues into VFX, SFX, camera or UI. One method, with
    /// the event inside the parameters, so a presenter that pools keys its
    /// instances by <see cref="GameplayCueParameters.Handle"/> and gives them
    /// back on <see cref="GameplayCueEvent.Remove"/>.
    ///
    /// A presenter must never change gameplay state. The parameters hand it no
    /// runtime object to change: definitions, actors, numbers and geometry,
    /// all read-only. An exception thrown here is logged and swallowed; the
    /// activation it interrupted carries on.
    /// </summary>
    public interface IGameplayCuePresenter
    {
        void OnGameplayCue(in GameplayCueParameters parameters);
    }

    /// <summary>
    /// A hook between the raise and the presenters. It may rewrite the
    /// parameters — replace the tag, move the location — or return false to
    /// suppress the cue entirely. A game hangs its block, parry, immunity and
    /// miss rules here when the effect's own replacements are not enough.
    /// </summary>
    public interface IGameplayCueFilter
    {
        bool Filter(ref GameplayCueParameters parameters);
    }
}
