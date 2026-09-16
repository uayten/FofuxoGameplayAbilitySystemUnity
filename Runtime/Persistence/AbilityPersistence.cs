using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// Writes an actor's ability state down and puts it back. Everything here
    /// is static and explicit: a game that never saves never calls it, and
    /// pays no serialized field, no component and no tick for its existence.
    /// </summary>
    /// <remarks>
    /// What travels: cooldowns, charges and their restore timers, attribute
    /// base values, active duration and infinite effects with their stacks,
    /// and loose tags. What does not: running activations, tasks, animator
    /// state and the source actor of an effect. A save is taken between
    /// activations, and a GameObject is not data.
    /// </remarks>
    public static class AbilityPersistence
    {
        /// <summary>Captures an actor without asking for the report.</summary>
        public static AbilitySaveRecord Capture(AbilitySystem system)
        {
            return Capture(system, out _);
        }

        /// <summary>
        /// Captures everything the actor's ability state owns that outlives a
        /// session. The record is a plain object: the caller decides whether it
        /// becomes a file, a slot in a larger save, or nothing at all.
        /// </summary>
        public static AbilitySaveRecord Capture(AbilitySystem system, out AbilitySaveReport report)
        {
            report = new AbilitySaveReport();
            if (system == null)
            {
                report.Warn("There is no ability system to capture.");
                return null;
            }

            AbilitySaveRecord record = new()
            {
                Version = AbilitySaveRecord.CurrentVersion,
                SavedAtUtcTicks = DateTime.UtcNow.Ticks
            };

            if (system.IsActive)
            {
                report.Warn(
                    $"'{system.name}' was captured while {system.ActiveAbilityCount} " +
                    "activation(s) were running; a running ability is not part of a record.");
            }

            record.Abilities = CaptureAbilities(system, report);
            record.Attributes = CaptureAttributes(system, report);
            record.Effects = CaptureEffects(system, report);
            record.LooseTags = CaptureLooseTags(system);
            return record;
        }

        /// <summary>Restores an actor with the default options.</summary>
        public static bool Restore(AbilitySystem system, AbilitySaveRecord record)
        {
            return Restore(system, record, null, out _);
        }

        /// <summary>
        /// Puts a record back on an actor. The record is migrated to the
        /// current schema first, and a record that cannot be migrated is
        /// refused rather than half-applied.
        /// </summary>
        /// <returns>False when the record could not be read at all.</returns>
        public static bool Restore(
            AbilitySystem system,
            AbilitySaveRecord record,
            AbilityRestoreOptions options,
            out AbilitySaveReport report)
        {
            report = new AbilitySaveReport();
            if (system == null)
            {
                report.Warn("There is no ability system to restore onto.");
                return false;
            }

            if (record == null)
            {
                report.Warn("There is no record to restore.");
                return false;
            }

            if (!AbilitySaveMigrator.TryUpgrade(record, out string migrationError))
            {
                report.Warn(migrationError);
                return false;
            }

            options ??= new AbilityRestoreOptions();
            IAbilitySaveResolver resolver = options.Resolver ?? new AbilitySaveResolver(system);
            double offline = ResolveOfflineSeconds(record, options);
            report.OfflineSecondsApplied = offline;

            if (options.ClearExistingState)
            {
                system.ClearSavedState();
                GameplayEffectContainer existing = GameplayEffectContainer.Find(system.gameObject);
                if (existing != null)
                {
                    existing.RemoveAllEffects();
                }
            }

            RestoreAbilities(system, record, resolver, options, offline, report);
            RestoreAttributes(system, record, report);
            RestoreEffects(system, record, resolver, options, offline, report);
            RestoreLooseTags(system, record);
            return true;
        }

        /// <summary>
        /// Real seconds between when the record was captured and now. This is
        /// the number a day/night cycle, a regrowing resource or any other
        /// offline progression is built on; what it means is the game's
        /// decision, not the package's.
        /// </summary>
        public static double SecondsSince(AbilitySaveRecord record, DateTime? now = null)
        {
            if (record == null || record.SavedAtUtcTicks <= 0L)
            {
                return 0d;
            }

            DateTime reference = now?.ToUniversalTime() ?? DateTime.UtcNow;
            double seconds = (reference - record.SavedAtUtc).TotalSeconds;
            return seconds > 0d ? seconds : 0d;
        }

        /// <summary>Serializes a record with Unity's own JSON writer.</summary>
        public static string ToJson(AbilitySaveRecord record, bool prettyPrint = false)
        {
            return record == null ? string.Empty : JsonUtility.ToJson(record, prettyPrint);
        }

        /// <summary>Reads a record back. Null when the text is not one.</summary>
        public static AbilitySaveRecord FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                return JsonUtility.FromJson<AbilitySaveRecord>(json);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private static AbilitySaveAbilityEntry[] CaptureAbilities(
            AbilitySystem system, AbilitySaveReport report)
        {
            List<AbilitySaveAbilityEntry> entries = new();
            if (system.Loadout == null)
            {
                return entries.ToArray();
            }

            foreach (AbilityDefinition ability in system.Loadout.Abilities)
            {
                if (ability == null)
                {
                    continue;
                }

                float cooldown = system.GetCooldownRemaining(ability);
                bool limited = ability.HasLimitedCharges;
                float charges = limited ? system.GetCharges(ability) : -1f;
                system.ChargeRestoreElapsed.TryGetValue(ability, out float restoreElapsed);

                bool remembersSomething =
                    cooldown > 0f || (limited && charges < ability.MaxCharges);
                if (!remembersSomething)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(ability.AbilityId))
                {
                    report.Warn(
                        $"'{ability.name}' has no ability id and was not saved.");
                    continue;
                }

                entries.Add(new AbilitySaveAbilityEntry
                {
                    AbilityId = ability.AbilityId,
                    CooldownRemaining = cooldown,
                    Charges = charges,
                    ChargeRestoreElapsed = restoreElapsed
                });
            }

            report.AbilitiesHandled = entries.Count;
            return entries.ToArray();
        }

        private static AbilitySaveAttributeEntry[] CaptureAttributes(
            AbilitySystem system, AbilitySaveReport report)
        {
            AttributeSet attributes = system.GetComponent<AttributeSet>();
            if (attributes == null)
            {
                return Array.Empty<AbilitySaveAttributeEntry>();
            }

            List<AbilitySaveAttributeEntry> entries = new();
            foreach (KeyValuePair<GameplayAttribute, AttributeValue> pair in attributes.Values)
            {
                if (pair.Key.IsEmpty)
                {
                    continue;
                }

                entries.Add(new AbilitySaveAttributeEntry
                {
                    AttributeId = pair.Key.Id,
                    BaseValue = pair.Value.BaseValue
                });
            }

            report.AttributesHandled = entries.Count;
            return entries.ToArray();
        }

        private static AbilitySaveEffectEntry[] CaptureEffects(
            AbilitySystem system, AbilitySaveReport report)
        {
            GameplayEffectContainer container = GameplayEffectContainer.Find(system.gameObject);
            if (container == null)
            {
                return Array.Empty<AbilitySaveEffectEntry>();
            }

            List<AbilitySaveEffectEntry> entries = new();
            foreach (ActiveGameplayEffect effect in container.ActiveEffects)
            {
                GameplayEffectDefinition definition = effect.Definition;
                if (definition == null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(definition.EffectId))
                {
                    report.Warn(
                        $"'{definition.name}' has no effect id and was not saved; " +
                        "an effect that outlives the frame it lands on needs one.");
                    continue;
                }

                entries.Add(new AbilitySaveEffectEntry
                {
                    EffectId = definition.EffectId,
                    Level = effect.Level,
                    StackCount = effect.StackCount,
                    Infinite = effect.IsInfinite,
                    TotalDuration = effect.IsInfinite ? 0f : effect.TotalDuration,
                    RemainingDuration = effect.IsInfinite ? 0f : effect.RemainingDuration,
                    Period = effect.Period,
                    PeriodAccumulator = effect.PeriodAccumulator,
                    PeriodCount = effect.PeriodCount
                });
            }

            report.EffectsHandled = entries.Count;
            return entries.ToArray();
        }

        private static string[] CaptureLooseTags(AbilitySystem system)
        {
            IReadOnlyCollection<GameplayTag> tags = system.LooseTags;
            if (tags.Count == 0)
            {
                return Array.Empty<string>();
            }

            List<string> values = new(tags.Count);
            foreach (GameplayTag tag in tags)
            {
                if (!tag.IsEmpty)
                {
                    values.Add(tag.Value);
                }
            }

            return values.ToArray();
        }

        private static double ResolveOfflineSeconds(
            AbilitySaveRecord record, AbilityRestoreOptions options)
        {
            if (options.OfflinePolicy != AbilityOfflinePolicy.Advance)
            {
                return 0d;
            }

            return options.OfflineSeconds >= 0d
                ? options.OfflineSeconds
                : SecondsSince(record);
        }

        private static void RestoreAbilities(
            AbilitySystem system,
            AbilitySaveRecord record,
            IAbilitySaveResolver resolver,
            AbilityRestoreOptions options,
            double offline,
            AbilitySaveReport report)
        {
            int restored = 0;
            foreach (AbilitySaveAbilityEntry entry in record.Abilities)
            {
                if (entry == null)
                {
                    continue;
                }

                AbilityDefinition ability = resolver.ResolveAbility(entry.AbilityId);
                if (ability == null)
                {
                    report.Warn(
                        $"Ability '{entry.AbilityId}' is in the record and not in the " +
                        "loadout; its cooldown and charges were dropped.");
                    continue;
                }

                float cooldown = AgeCooldown(entry.CooldownRemaining, options, offline);
                system.RestoreCooldown(ability, cooldown);
                RestoreCharges(system, ability, entry, options, offline, cooldown);
                restored++;
            }

            report.AbilitiesHandled = restored;
        }

        private static float AgeCooldown(
            float remaining, AbilityRestoreOptions options, double offline)
        {
            return options.OfflinePolicy switch
            {
                AbilityOfflinePolicy.Expire => 0f,
                AbilityOfflinePolicy.Advance => (float)Math.Max(0d, remaining - offline),
                _ => Mathf.Max(0f, remaining)
            };
        }

        private static void RestoreCharges(
            AbilitySystem system,
            AbilityDefinition ability,
            AbilitySaveAbilityEntry entry,
            AbilityRestoreOptions options,
            double offline,
            float cooldownRemaining)
        {
            if (!ability.HasLimitedCharges || !entry.HasCharges)
            {
                return;
            }

            if (options.OfflinePolicy == AbilityOfflinePolicy.Expire)
            {
                system.RestoreCharges(ability, ability.MaxCharges, 0f);
                return;
            }

            float charges = Mathf.Clamp(entry.Charges, 0f, ability.MaxCharges);
            float restoreElapsed = Mathf.Max(0f, entry.ChargeRestoreElapsed);

            if (options.OfflinePolicy == AbilityOfflinePolicy.Advance && charges < ability.MaxCharges)
            {
                if (ability.ChargeRestoreTime > 0f)
                {
                    double banked = restoreElapsed + offline;
                    double restored = Math.Floor(banked / ability.ChargeRestoreTime);
                    double missing = ability.MaxCharges - charges;
                    double gained = Math.Min(restored, missing);
                    charges += (float)gained;
                    restoreElapsed = charges >= ability.MaxCharges
                        ? 0f
                        : (float)(banked - gained * ability.ChargeRestoreTime);
                }
                else if (cooldownRemaining <= 0f)
                {
                    charges = ability.MaxCharges;
                    restoreElapsed = 0f;
                }
            }

            system.RestoreCharges(ability, charges, restoreElapsed);
        }

        private static void RestoreAttributes(
            AbilitySystem system, AbilitySaveRecord record, AbilitySaveReport report)
        {
            if (record.Attributes.Length == 0)
            {
                return;
            }

            AttributeSet attributes = system.GetComponent<AttributeSet>();
            if (attributes == null)
            {
                report.Warn(
                    $"'{system.name}' has no attribute set; " +
                    $"{record.Attributes.Length} attribute value(s) were dropped.");
                return;
            }

            int restored = 0;
            foreach (AbilitySaveAttributeEntry entry in record.Attributes)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.AttributeId))
                {
                    continue;
                }

                attributes.ApplyInstantModifier(new AttributeModifier(
                    new GameplayAttribute(entry.AttributeId),
                    AttributeOperation.Override,
                    entry.BaseValue,
                    system));
                restored++;
            }

            report.AttributesHandled = restored;
        }

        private static void RestoreEffects(
            AbilitySystem system,
            AbilitySaveRecord record,
            IAbilitySaveResolver resolver,
            AbilityRestoreOptions options,
            double offline,
            AbilitySaveReport report)
        {
            if (record.Effects.Length == 0)
            {
                return;
            }

            GameplayEffectContainer container = GameplayEffectContainer.For(system.gameObject);
            int restored = 0;
            foreach (AbilitySaveEffectEntry entry in record.Effects)
            {
                if (entry == null)
                {
                    continue;
                }

                GameplayEffectDefinition definition = resolver.ResolveEffect(entry.EffectId);
                if (definition == null)
                {
                    report.Warn(
                        $"Effect '{entry.EffectId}' is in the record and the resolver " +
                        "does not know it; it was dropped.");
                    continue;
                }

                float remaining = 0f;
                if (!entry.Infinite && !TryAgeDuration(entry, options, offline, out remaining))
                {
                    continue;
                }

                GameplayEffectSpec spec =
                    new GameplayEffectSpec(definition, null, system.gameObject, entry.Level)
                        .SetDuration(entry.TotalDuration)
                        .SetPeriod(entry.Period);

                container.RestoreActiveEffect(
                    spec,
                    entry.TotalDuration,
                    remaining,
                    entry.Infinite,
                    Mathf.Max(1, entry.StackCount),
                    Mathf.Max(0f, entry.PeriodAccumulator),
                    Mathf.Max(0, entry.PeriodCount));
                restored++;
            }

            report.EffectsHandled = restored;
        }

        /// <summary>
        /// Ages one timed effect. False when it ran out while the game was
        /// closed, which is not a warning: an expired effect coming back
        /// expired is the policy working.
        /// </summary>
        private static bool TryAgeDuration(
            AbilitySaveEffectEntry entry,
            AbilityRestoreOptions options,
            double offline,
            out float remaining)
        {
            switch (options.OfflinePolicy)
            {
                case AbilityOfflinePolicy.Expire:
                    remaining = 0f;
                    return false;

                case AbilityOfflinePolicy.Advance:
                    remaining = (float)(entry.RemainingDuration - offline);
                    return remaining > 0f;

                default:
                    remaining = entry.RemainingDuration;
                    return remaining > 0f;
            }
        }

        private static void RestoreLooseTags(AbilitySystem system, AbilitySaveRecord record)
        {
            foreach (string value in record.LooseTags)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    system.SetLooseTag(new GameplayTag(value), true);
                }
            }
        }
    }
}
