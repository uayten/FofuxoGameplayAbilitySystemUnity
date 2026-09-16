using System;
using System.Collections.Generic;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// What the time between the save and the load does to everything that was
    /// counting down. The package has no opinion on which of these a game wants
    /// and refuses to pick one silently.
    /// </summary>
    public enum AbilityOfflinePolicy
    {
        /// <summary>
        /// No time passed. A cooldown with four seconds left resumes with four
        /// seconds left, whichever day the save is opened on.
        /// </summary>
        Freeze,

        /// <summary>
        /// Time passed and is charged to everything timed: cooldowns, charge
        /// timers and effect durations are aged by
        /// <see cref="AbilityRestoreOptions.OfflineSeconds"/>, and whatever ran
        /// out while the game was closed comes back finished.
        /// </summary>
        Advance,

        /// <summary>
        /// Time passed and the wait is over: no cooldown survives, charges come
        /// back full, and every timed effect is dropped. An infinite effect has
        /// no clock and survives all three policies.
        /// </summary>
        Expire
    }

    /// <summary>How one record is put back on an actor.</summary>
    public sealed class AbilityRestoreOptions
    {
        /// <summary>
        /// Where identifiers become assets. Null means the actor's own loadout,
        /// through <see cref="AbilitySaveResolver"/>.
        /// </summary>
        public IAbilitySaveResolver Resolver { get; set; }

        /// <summary>What elapsed time does to cooldowns, charges and durations.</summary>
        public AbilityOfflinePolicy OfflinePolicy { get; set; } = AbilityOfflinePolicy.Freeze;

        /// <summary>
        /// The elapsed seconds <see cref="AbilityOfflinePolicy.Advance"/> spends.
        /// Negative means "ask the record", which measures against
        /// <see cref="DateTime.UtcNow"/>.
        /// </summary>
        public double OfflineSeconds { get; set; } = -1d;

        /// <summary>
        /// Whether restoring clears what the actor already had. On - the
        /// default - a load replaces the actor's state; off, the record is
        /// layered onto it.
        /// </summary>
        public bool ClearExistingState { get; set; } = true;
    }

    /// <summary>
    /// What a capture or a restore actually did. Every entry the package could
    /// not honour is named here rather than dropped quietly: an effect with no
    /// id, an ability that left the loadout, a migration that does not exist.
    /// </summary>
    public sealed class AbilitySaveReport
    {
        private readonly List<string> warnings = new();

        /// <summary>Abilities written down, or put back.</summary>
        public int AbilitiesHandled { get; internal set; }

        /// <summary>Attributes written down, or put back.</summary>
        public int AttributesHandled { get; internal set; }

        /// <summary>Active effects written down, or put back.</summary>
        public int EffectsHandled { get; internal set; }

        /// <summary>Seconds of offline time the restore charged to the record.</summary>
        public double OfflineSecondsApplied { get; internal set; }

        /// <summary>Everything the package could not honour, in the order it was found.</summary>
        public IReadOnlyList<string> Warnings => warnings;

        public bool HasWarnings => warnings.Count > 0;

        internal void Warn(string message)
        {
            warnings.Add(message);
        }

        public override string ToString()
        {
            return $"{AbilitiesHandled} abilities, {AttributesHandled} attributes, " +
                   $"{EffectsHandled} effects, {warnings.Count} warnings";
        }
    }
}
