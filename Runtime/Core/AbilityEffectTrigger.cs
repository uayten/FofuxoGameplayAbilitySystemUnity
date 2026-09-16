using System;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// One effect fired at one frame of a step. This is where the damage frame
    /// lives - not in the phase windows.
    /// </summary>
    [Serializable]
    public struct AbilityEffectTrigger
    {
        [Tooltip("1-based frame of the step's timeline where the effect is applied. This is the damage frame: pick the frame where the weapon reaches the target.")]
        [SerializeField, Min(1)] private int frame;
        [Tooltip("Effect applied on that frame. A damage effect resolves against whatever the ability's targeting finds.")]
        [SerializeField] private GameplayEffectDefinition effect;
        [Tooltip("Magnitude level supplied to the effect. Legacy value zero resolves to level 1.")]
        [SerializeField, Range(1, 100)] private int level;

        public AbilityEffectTrigger(int frame, GameplayEffectDefinition effect, int level = 1)
        {
            this.frame = frame;
            this.effect = effect;
            this.level = level;
        }

        public int Frame => Mathf.Max(1, frame);
        public GameplayEffectDefinition Effect => effect;
        public int Level => Mathf.Clamp(level <= 0 ? 1 : level, 1, 100);
    }
}
