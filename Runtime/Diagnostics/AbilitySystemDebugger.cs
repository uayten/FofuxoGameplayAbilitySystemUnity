using System.Text;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// Lightweight runtime readout for tuning: logs ability and cue events and
    /// exposes a one-line summary (active ability, frame, tags) for Inspector
    /// monitoring. The full readout is the Ability Debugger window
    /// (Window > Fofuxo > Ability Debugger), which reads the actor's
    /// <see cref="AbilitySystem.History"/>; this component is the one that
    /// leaves a trail in the Console.
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

                AbilityDefinition ability = abilitySystem.ActiveAbility;
                var timeline = ability as TimelineAbilityDefinition;
                string active = ability == null ? "(idle)" : ability.AbilityId;
                string step = timeline != null && timeline.IsCombo
                    ? $" step={abilitySystem.ActiveStepIndex + 1}/{timeline.StepCount}"
                    : string.Empty;
                // Only worth a line while a combo is waiting for something: what
                // is buffered, and how many frames are left to buffer it in.
                string intent = timeline != null && timeline.WaitsToAdvance
                    ? $" intent={abilitySystem.QueuedStepIntent.Status}" +
                      $" deadline={abilitySystem.ComboInputDeadlineFrame}" +
                      (abilitySystem.IsHoldingLastStep ? " holding" : string.Empty)
                    : string.Empty;
                string concurrent = abilitySystem.ActiveAbilityCount > 1
                    ? $" +{abilitySystem.ActiveAbilityCount - 1} concurrent"
                    : string.Empty;
                return $"{active}{step}{intent}{concurrent} " +
                       $"frame={abilitySystem.ActiveFrame} tags=[{tags}] " +
                       $"last={abilitySystem.LastTransition.Reason}";
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

        private void OnAbilityCancelled(AbilityDefinition ability, GameplayTag cancelTag)
        {
            Log($"cancelled {ability.AbilityId} ({cancelTag})");
        }

        private void OnAbilityWhiffed(AbilityDefinition ability, AbilityContext _)
        {
            Log($"whiffed {ability.AbilityId}");
        }

        private void OnGameplayCueTriggered(GameplayCueParameters cue)
        {
            Log($"cue {cue.Cue.Value} {cue.Event} {cue.Outcome}");
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
