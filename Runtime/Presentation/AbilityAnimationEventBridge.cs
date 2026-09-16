using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// Forwards Unity Animation Events to the ability system.
    ///
    /// Two destinations, and the difference is the whole point.
    /// <see cref="EmitGameplayCue"/> fires a cosmetic cue and can never change
    /// gameplay state. <see cref="EmitAbilityAnimationEvent"/> wakes a
    /// <see cref="WaitAnimationEventTask"/>, which can, so it stays a separate
    /// call an author opts into rather than something every cue does by
    /// accident.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AbilityAnimationEventBridge : MonoBehaviour
    {
        /// <summary>
        /// Add an Animation Event on any clip calling this with a cue tag such
        /// as <c>Cue.Footstep</c>.
        /// </summary>
        public void EmitGameplayCue(string cueTag)
        {
            if (string.IsNullOrWhiteSpace(cueTag))
            {
                return;
            }

            if (!TryGetAbilitySystem(out AbilitySystem abilitySystem))
            {
                return;
            }

            abilitySystem.TriggerGameplayCue(
                new GameplayTag(cueTag),
                AbilityContext.FromTarget(gameObject, null));
        }

        /// <summary>
        /// Add an Animation Event calling this with the name a
        /// <see cref="WaitAnimationEventTask"/> is waiting for. The event only
        /// releases tasks; it never advances a step or fires an effect, because
        /// Animator state is not authoritative for gameplay timing.
        /// </summary>
        public void EmitAbilityAnimationEvent(string eventName)
        {
            if (string.IsNullOrWhiteSpace(eventName))
            {
                return;
            }

            if (!TryGetAbilitySystem(out AbilitySystem abilitySystem))
            {
                return;
            }

            abilitySystem.NotifyAnimationEvent(eventName);
        }

        private bool TryGetAbilitySystem(out AbilitySystem abilitySystem)
        {
            abilitySystem = GetComponent<AbilitySystem>();
            if (abilitySystem != null)
            {
                return true;
            }

            Debug.LogWarning(
                $"AbilityAnimationEventBridge on '{name}' needs an AbilitySystem.", this);
            return false;
        }
    }
}
