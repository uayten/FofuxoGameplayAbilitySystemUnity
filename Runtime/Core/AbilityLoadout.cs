using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// What makes a loadout invalid. Ordered from the cheapest mistake to spot
    /// to the one that costs the most to debug at runtime.
    /// </summary>
    public enum AbilityLoadoutIssue
    {
        /// <summary>A slot in the list holds no ability.</summary>
        EmptySlot,
        /// <summary>An ability in the list carries no ability ID.</summary>
        EmptyAbilityId,
        /// <summary>Two abilities in the list share an ability ID.</summary>
        DuplicateAbilityId,
        /// <summary>An ability in the list fails its own validation.</summary>
        InvalidAbility
    }

    /// <summary>
    /// One problem found in a loadout, addressed to the slot that caused it so
    /// the Inspector can point at a row instead of at the asset.
    /// </summary>
    public readonly struct AbilityLoadoutValidationIssue
    {
        public AbilityLoadoutValidationIssue(
            AbilityLoadoutIssue issue,
            int slotIndex,
            AbilityDefinition ability,
            string message)
        {
            Issue = issue;
            SlotIndex = slotIndex;
            Ability = ability;
            Message = message;
        }

        public AbilityLoadoutIssue Issue { get; }

        /// <summary>Zero-based index into <see cref="AbilityLoadout.Abilities"/>.</summary>
        public int SlotIndex { get; }

        /// <summary>The offending ability, or null for an empty slot.</summary>
        public AbilityDefinition Ability { get; }

        public string Message { get; }

        public override string ToString() => Message;
    }
    /// <summary>
    /// The abilities an actor is granted, in the order gameplay events are
    /// offered to them. An asset, so a character swaps its whole kit in one
    /// field.
    /// </summary>
    [CreateAssetMenu(fileName = "GAL_NewLoadout", menuName = "Fofuxo/Abilities/Loadout")]
    public sealed class AbilityLoadout : ScriptableObject
    {
        [SerializeField] private AbilityDefinition[] abilities = { };

        public IReadOnlyList<AbilityDefinition> Abilities => abilities;

        public bool Contains(AbilityDefinition ability)
        {
            return ability != null && Array.IndexOf(abilities, ability) >= 0;
        }

        public AbilityDefinition FindAbility(string abilityId)
        {
            if (string.IsNullOrWhiteSpace(abilityId))
            {
                return null;
            }

            foreach (AbilityDefinition ability in abilities)
            {
                if (ability != null &&
                    string.Equals(ability.AbilityId, abilityId, StringComparison.Ordinal))
                {
                    return ability;
                }
            }

            return null;
        }

        /// <summary>
        /// The first problem found, in the shape
        /// <see cref="AbilityDefinition.TryValidate"/> already uses. Use
        /// <see cref="TryValidate(List{AbilityLoadoutValidationIssue})"/> when
        /// every problem has to be shown at once.
        /// </summary>
        public bool TryValidate(out string error)
        {
            List<AbilityLoadoutValidationIssue> issues = new(1);
            if (Collect(issues, stopAtFirstIssue: true))
            {
                error = null;
                return true;
            }

            error = issues[0].Message;
            return false;
        }

        /// <summary>
        /// The full report. <paramref name="issues"/> is cleared first and left
        /// empty when the loadout is valid, so the same list can be reused
        /// across Inspector repaints without allocating.
        /// </summary>
        /// <returns>True when the loadout has no problem at all.</returns>
        public bool TryValidate(List<AbilityLoadoutValidationIssue> issues)
        {
            if (issues == null)
            {
                throw new ArgumentNullException(nameof(issues));
            }

            issues.Clear();
            return Collect(issues, stopAtFirstIssue: false);
        }

        /// <summary>
        /// The single implementation of the loadout rules. Runtime callers, the
        /// Inspector audit, and the tests all reach the checks through here so
        /// there is never a second copy to keep in step.
        /// </summary>
        private bool Collect(List<AbilityLoadoutValidationIssue> issues, bool stopAtFirstIssue)
        {
            if (abilities == null || abilities.Length == 0)
            {
                return true;
            }

            // Ordinal, because that is what FindAbility compares with: two IDs
            // that differ only in case are two different lookups, not a clash.
            Dictionary<string, int> slotByAbilityId = new(abilities.Length, StringComparer.Ordinal);

            for (int i = 0; i < abilities.Length; i++)
            {
                AbilityDefinition ability = abilities[i];
                if (ability == null)
                {
                    issues.Add(new AbilityLoadoutValidationIssue(
                        AbilityLoadoutIssue.EmptySlot,
                        i,
                        null,
                        $"Slot {i + 1} is empty."));
                    if (stopAtFirstIssue)
                    {
                        return false;
                    }

                    continue;
                }

                string abilityId = ability.AbilityId;
                if (string.IsNullOrWhiteSpace(abilityId))
                {
                    issues.Add(new AbilityLoadoutValidationIssue(
                        AbilityLoadoutIssue.EmptyAbilityId,
                        i,
                        ability,
                        $"Slot {i + 1} ('{ability.name}') has no ability ID. " +
                        "It can never be found by ID."));
                    if (stopAtFirstIssue)
                    {
                        return false;
                    }

                    // TryValidate would only repeat the missing ID as its first
                    // error, so the rest of the definition waits until the ID
                    // is filled in.
                    continue;
                }
                else if (slotByAbilityId.TryGetValue(abilityId, out int firstSlot))
                {
                    issues.Add(new AbilityLoadoutValidationIssue(
                        AbilityLoadoutIssue.DuplicateAbilityId,
                        i,
                        ability,
                        $"Slot {i + 1} ('{ability.name}') repeats the ability ID " +
                        $"'{abilityId}', already used by slot {firstSlot + 1} " +
                        $"('{NameOf(abilities[firstSlot])}'). FindAbility only ever " +
                        "returns the first."));
                    if (stopAtFirstIssue)
                    {
                        return false;
                    }
                }
                else
                {
                    slotByAbilityId.Add(abilityId, i);
                }

                if (!ability.TryValidate(out string abilityError))
                {
                    issues.Add(new AbilityLoadoutValidationIssue(
                        AbilityLoadoutIssue.InvalidAbility,
                        i,
                        ability,
                        $"Slot {i + 1} ('{ability.name}') is invalid: {abilityError}"));
                    if (stopAtFirstIssue)
                    {
                        return false;
                    }
                }
            }

            return issues.Count == 0;
        }

        private static string NameOf(AbilityDefinition ability)
        {
            return ability == null ? "empty" : ability.name;
        }
    }
}
