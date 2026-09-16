using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// Where an application comes from: the system applying it, the activation
    /// that fired the trigger, which trigger it was, and at what level.
    ///
    /// This is not the per-application data — that is
    /// <see cref="GameplayEffectSpec"/>, one per target. This is the request
    /// that produces those specs, and it carries no result.
    /// </summary>
    public readonly struct GameplayEffectContext
    {
        public GameplayEffectContext(
            AbilitySystem abilitySystem,
            AbilityInstance instance,
            int triggerIndex,
            int level = 1)
        {
            AbilitySystem = abilitySystem;
            Instance = instance;
            TriggerIndex = triggerIndex;
            Level = Mathf.Clamp(level <= 0 ? 1 : level, 1, 100);
            Definition = instance?.Definition;
            AbilityContext = instance?.Context ?? default;
        }

        /// <summary>
        /// An application with no activation behind it: a parry reward, a
        /// cleanse, anything a consumer applies directly. Nothing here owns a
        /// task scope, so nothing it starts can outlive an activation that was
        /// invented to host it.
        /// </summary>
        public GameplayEffectContext(
            AbilitySystem abilitySystem,
            in AbilityContext abilityContext,
            AbilityDefinition sourceAbility = null,
            int level = 1)
        {
            AbilitySystem = abilitySystem;
            Instance = null;
            TriggerIndex = -1;
            Level = Mathf.Clamp(level <= 0 ? 1 : level, 1, 100);
            Definition = sourceAbility;
            AbilityContext = abilityContext;
        }

        public AbilitySystem AbilitySystem { get; }

        /// <summary>The activation that fired the trigger, or null outside a timeline.</summary>
        public AbilityInstance Instance { get; }

        /// <summary>Index of the step effect trigger, or -1 when there is none.</summary>
        public int TriggerIndex { get; }

        /// <summary>Magnitude level, 1 to 100. A trigger serialized as 0 resolves to 1.</summary>
        public int Level { get; }

        /// <summary>The ability the effect was applied from, when there was one.</summary>
        public AbilityDefinition Definition { get; }

        public AbilityContext AbilityContext { get; }
        public GameObject Owner => AbilityContext.Owner;
        public GameObject Target => AbilityContext.Target;
    }
}
