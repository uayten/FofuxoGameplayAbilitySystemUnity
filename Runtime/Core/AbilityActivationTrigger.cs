using System;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// Where an automatic activation comes from. Gameplay events are the only
    /// source today; the enum exists so a second one is an added case rather
    /// than a new field.
    /// </summary>
    public enum AbilityActivationTriggerSource
    {
        GameplayEvent
    }

    /// <summary>
    /// Data-driven automatic activation. Gameplay events carry the context;
    /// the triggered ability remains a normal granted ability with the same
    /// validation, tags, lifecycle, and cleanup as an explicit activation.
    /// </summary>
    [Serializable]
    public struct AbilityActivationTrigger
    {
        [SerializeField] private AbilityActivationTriggerSource source;
        [Tooltip("Event tag that activates this ability.")]
        [SerializeField] private GameplayTag tag;
        [Tooltip("Cancel the currently active ability before activating this one.")]
        [SerializeField] private bool interruptActiveAbility;
        [Tooltip("A repeated event while this ability is already running restarts it: the running activation ends as superseded and a fresh one starts with the new event. Off, the repeat is consumed and the running activation carries on. A hit reaction wants the restart; a physics reaction, whose body is still flying, does not.")]
        [SerializeField] private bool restartWhenActive;

        public AbilityActivationTrigger(
            GameplayTag tag,
            bool interruptActiveAbility = false,
            AbilityActivationTriggerSource source = AbilityActivationTriggerSource.GameplayEvent,
            bool restartWhenActive = false)
        {
            this.source = source;
            this.tag = tag;
            this.interruptActiveAbility = interruptActiveAbility;
            this.restartWhenActive = restartWhenActive;
        }

        public AbilityActivationTriggerSource Source => source;
        public GameplayTag Tag => tag;
        public bool InterruptActiveAbility => interruptActiveAbility;
        public bool RestartsWhenActive => restartWhenActive;

        /// <summary>Whether this trigger answers that event tag.</summary>
        public bool Matches(GameplayTag eventTag)
        {
            return source == AbilityActivationTriggerSource.GameplayEvent &&
                   !tag.IsEmpty &&
                   tag == eventTag;
        }
    }
}
