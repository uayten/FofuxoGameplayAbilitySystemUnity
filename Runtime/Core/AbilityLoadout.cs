using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    [CreateAssetMenu(fileName = "AbilityLoadout", menuName = "Fofuxo/Abilities/Loadout")]
    public sealed class AbilityLoadout : ScriptableObject
    {
        [SerializeField] private AbilityDefinition[] abilities = { };
        [SerializeField] private AbilitySequenceDefinition[] sequences = { };

        public IReadOnlyList<AbilityDefinition> Abilities => abilities;
        public IReadOnlyList<AbilitySequenceDefinition> Sequences => sequences;

        public bool Contains(AbilityDefinition ability)
        {
            return ability != null && Array.IndexOf(abilities, ability) >= 0;
        }

        public bool Contains(AbilitySequenceDefinition sequence)
        {
            return sequence != null && Array.IndexOf(sequences, sequence) >= 0;
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
    }
}
