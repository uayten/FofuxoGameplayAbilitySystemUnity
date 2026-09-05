using System.Text;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// Lightweight runtime readout for tuning: logs ability and cue events and
    /// exposes a one-line summary (active ability, frame, tags) for Inspector
    /// monitoring. Not a substitute for the planned runtime debugger window.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AbilitySystem))]
    public sealed class AbilitySystemDebugger : MonoBehaviour
    {
        [SerializeField] private bool logTransitions = true;

        private AbilitySystem abilitySystem;

        public string Summary
        {
            get
            {
                if (abilitySystem == null)
                {
                    return "(no AbilitySystem)";
                }

                var tags = new StringBuilder();
                foreach (GameplayTag tag in abilitySystem.ActiveTags)
                {
                    if (tags.Length > 0)
                    {
                        tags.Append(", ");
                    }

                    tags.Append(tag.Value);
                }

                string active = abilitySystem.ActiveAbility != null
                    ? abilitySystem.ActiveAbility.AbilityId
                    : abilitySystem.ActiveSequence != null
                        ? "sequence:" + abilitySystem.ActiveSequence.SequenceId
                        : "(idle)";
                return $"{active} frame={abilitySystem.ActiveFrame} tags=[{tags}]";
            }
        }

        private void Awake()
        {
            abilitySystem = GetComponent<AbilitySystem>();
        }

        private void OnEnable()
        {
            if (abilitySystem == null)
            {
                return;
            }

            abilitySystem.AbilityStarted += OnAbilityStarted;
            abilitySystem.AbilityCompleted += OnAbilityCompleted;
            abilitySystem.AbilityCancelled += OnAbilityCancelled;
            abilitySystem.AbilityWhiffed += OnAbilityWhiffed;
            abilitySystem.GameplayCueTriggered += OnGameplayCueTriggered;
        }

        private void OnDisable()
        {
            if (abilitySystem == null)
            {
                return;
            }

            abilitySystem.AbilityStarted -= OnAbilityStarted;
            abilitySystem.AbilityCompleted -= OnAbilityCompleted;
            abilitySystem.AbilityCancelled -= OnAbilityCancelled;
            abilitySystem.AbilityWhiffed -= OnAbilityWhiffed;
            abilitySystem.GameplayCueTriggered -= OnGameplayCueTriggered;
        }

        private void OnAbilityStarted(AbilityDefinition ability)
        {
            Log($"started {ability.AbilityId}");
        }

        private void OnAbilityCompleted(AbilityDefinition ability)
        {
            Log($"completed {ability.AbilityId}");
        }

        private void OnAbilityCancelled(AbilityDefinition ability, AbilityCancelReason reason)
        {
            Log($"cancelled {ability.AbilityId} ({reason})");
        }

        private void OnAbilityWhiffed(AbilityDefinition ability, AbilityContext _)
        {
            Log($"whiffed {ability.AbilityId}");
        }

        private void OnGameplayCueTriggered(
            AbilityDefinition _,
            GameplayTag cue,
            AbilityContext __)
        {
            Log($"cue {cue.Value}");
        }

        private void Log(string message)
        {
            if (logTransitions)
            {
                Debug.Log($"[Ability] {name}: {message}", this);
            }
        }
    }
}
