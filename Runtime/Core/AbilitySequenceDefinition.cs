using System.Collections.Generic;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    public enum SequenceAdvancement
    {
        Automatic,
        Manual
    }

    [CreateAssetMenu(fileName = "AbilitySequence", menuName = "Fofuxo/Abilities/Sequence")]
    public sealed class AbilitySequenceDefinition : ScriptableObject
    {
        [SerializeField] private string sequenceId;
        [SerializeField] private AbilityDefinition[] steps = { };
        [SerializeField, Min(0f)] private float cooldown;
        [SerializeField] private SequenceAdvancement advancement;
        [Tooltip("Manual sequences wait this long for TryAdvanceSequence after a step. Zero waits indefinitely.")]
        [SerializeField, Min(0f)] private float manualAdvanceWindow;

        public string SequenceId => sequenceId ?? string.Empty;
        public IReadOnlyList<AbilityDefinition> Steps => steps;
        public float Cooldown => Mathf.Max(0f, cooldown);
        public SequenceAdvancement Advancement => advancement;
        public float ManualAdvanceWindow => Mathf.Max(0f, manualAdvanceWindow);

        private void OnValidate()
        {
            sequenceId = sequenceId?.Trim();
            cooldown = Mathf.Max(0f, cooldown);
            manualAdvanceWindow = Mathf.Max(0f, manualAdvanceWindow);
        }
    }
}
