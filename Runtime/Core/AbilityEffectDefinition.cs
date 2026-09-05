using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    public abstract class AbilityEffectDefinition : ScriptableObject
    {
        public abstract void Apply(AbilityEffectContext context);
    }

    public readonly struct AbilityEffectContext
    {
        public AbilityEffectContext(
            AbilitySystem abilitySystem,
            AbilityInstance instance,
            int triggerIndex)
        {
            AbilitySystem = abilitySystem;
            Instance = instance;
            TriggerIndex = triggerIndex;
        }

        public AbilitySystem AbilitySystem { get; }
        public AbilityInstance Instance { get; }
        public int TriggerIndex { get; }
        public AbilityDefinition Definition => Instance.Definition;
        public AbilityContext AbilityContext => Instance.Context;
        public GameObject Owner => Instance.Context.Owner;
        public GameObject Target => Instance.Context.Target;
    }
}
