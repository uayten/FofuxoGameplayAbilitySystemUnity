using System;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    [Serializable]
    public struct AbilityEffectTrigger
    {
        [SerializeField, Min(1)] private int frame;
        [SerializeField] private AbilityEffectDefinition effect;

        public int Frame => Mathf.Max(1, frame);
        public AbilityEffectDefinition Effect => effect;
    }
}
