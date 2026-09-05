using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// Forwards Unity Animation Events to the ability system as gameplay cues.
    /// Add an Animation Event on any clip calling
    /// <c>EmitGameplayCue</c> with a cue tag such as <c>Cue.Footstep</c>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AbilityAnimationEventBridge : MonoBehaviour
    {
        public void EmitGameplayCue(string cueTag)
        {
            if (string.IsNullOrWhiteSpace(cueTag))
            {
                return;
            }

            AbilitySystem abilitySystem = GetComponent<AbilitySystem>();
            if (abilitySystem == null)
            {
                Debug.LogWarning(
                    $"AbilityAnimationEventBridge on '{name}' needs an AbilitySystem.", this);
                return;
            }

            abilitySystem.TriggerGameplayCue(
                new GameplayTag(cueTag),
                AbilityContext.FromTarget(gameObject, null));
        }
    }
}
