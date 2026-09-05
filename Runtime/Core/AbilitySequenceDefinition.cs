using System.Collections.Generic;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    [CreateAssetMenu(fileName = "AbilitySequence", menuName = "Fofuxo/Abilities/Sequence")]
    public sealed class AbilitySequenceDefinition : ScriptableObject
    {
        [SerializeField] private string sequenceId;
        [SerializeField] private AbilityDefinition[] steps = { };
        [SerializeField, Min(0f)] private float cooldown;

        public string SequenceId => sequenceId ?? string.Empty;
        public IReadOnlyList<AbilityDefinition> Steps => steps;
        public float Cooldown => Mathf.Max(0f, cooldown);

        private void OnValidate()
        {
            sequenceId = sequenceId?.Trim();
            cooldown = Mathf.Max(0f, cooldown);
        }
    }
}
