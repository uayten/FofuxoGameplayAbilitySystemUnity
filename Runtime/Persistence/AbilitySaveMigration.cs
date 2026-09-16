using System;
using System.Collections.Generic;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// One step up the schema. A migration reads a record written by an older
    /// version of a game and leaves it shaped the way the next version expects.
    /// </summary>
    public interface IAbilitySaveMigration
    {
        /// <summary>
        /// The version this migration upgrades from. It always produces that
        /// version plus one.
        /// </summary>
        int FromVersion { get; }

        /// <summary>Rewrites the record in place.</summary>
        void Upgrade(AbilitySaveRecord record);
    }

    /// <summary>
    /// Walks a record from the version it was written with to
    /// <see cref="AbilitySaveRecord.CurrentVersion"/>, one registered migration
    /// at a time. A gap in the chain stops the load and says which version has
    /// no migration - restoring a record the package cannot read is worse than
    /// refusing it.
    /// </summary>
    public static class AbilitySaveMigrator
    {
        private static readonly Dictionary<int, IAbilitySaveMigration> migrations = new();

        /// <summary>Every registered migration, by the version it upgrades from.</summary>
        public static IReadOnlyDictionary<int, IAbilitySaveMigration> Migrations => migrations;

        /// <summary>Registers a migration, replacing one registered for the same version.</summary>
        public static void Register(IAbilitySaveMigration migration)
        {
            if (migration == null)
            {
                throw new ArgumentNullException(nameof(migration));
            }

            migrations[migration.FromVersion] = migration;
        }

        /// <summary>Forgets a registered migration.</summary>
        public static bool Unregister(int fromVersion)
        {
            return migrations.Remove(fromVersion);
        }

        /// <summary>Forgets every registered migration.</summary>
        public static void Clear()
        {
            migrations.Clear();
        }

        /// <summary>
        /// Brings a record up to the current version. A record already current
        /// is left untouched; one written by a newer version than this package
        /// knows is refused, because the fields it carries are not this
        /// schema's.
        /// </summary>
        public static bool TryUpgrade(AbilitySaveRecord record, out string error)
        {
            if (record == null)
            {
                error = "There is no record to upgrade.";
                return false;
            }

            if (record.Version > AbilitySaveRecord.CurrentVersion)
            {
                error = $"The record is version {record.Version} and this package " +
                        $"reads up to {AbilitySaveRecord.CurrentVersion}.";
                return false;
            }

            while (record.Version < AbilitySaveRecord.CurrentVersion)
            {
                if (!migrations.TryGetValue(record.Version, out IAbilitySaveMigration migration))
                {
                    error = $"No migration registered from save version {record.Version}.";
                    return false;
                }

                int from = record.Version;
                migration.Upgrade(record);
                record.Version = from + 1;
            }

            error = null;
            return true;
        }
    }
}
