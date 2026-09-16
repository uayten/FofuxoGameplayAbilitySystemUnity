using System;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// Fires a cosmetic cue tag at a fixed frame of an ability timeline.
    /// Cues never change gameplay state; game code presents them as VFX, SFX,
    /// or UI (the local equivalent of Unreal's GameplayCues).
    /// </summary>
    [Serializable]
    public struct GameplayCueTrigger
    {
        [Tooltip("1-based frame of the step's timeline where the cue fires.")]
        [SerializeField, Min(1)] private int frame;
        [Tooltip("Cosmetic cue tag presented by game code as VFX, SFX or UI. Never changes gameplay state.")]
        [SerializeField] private GameplayTag cue;
        [Tooltip("When no effect of this step has landed by this frame, raise nothing instead of a cue with the Missed outcome. The impact of a swing that met air.")]
        [SerializeField] private bool suppressOnMiss;

        public GameplayCueTrigger(int frame, GameplayTag cue, bool suppressOnMiss = false)
        {
            this.frame = frame;
            this.cue = cue;
            this.suppressOnMiss = suppressOnMiss;
        }

        public int Frame => Mathf.Max(1, frame);
        public GameplayTag Cue => cue;

        /// <summary>
        /// A cue that only makes sense on a hit. Effects apply before cues on
        /// the same frame, so the step already knows whether it landed anything
        /// when this is read. Off, a miss still raises the cue, carrying
        /// <see cref="GameplayCueOutcome.Missed"/> for a presenter or filter to
        /// swap in a whoosh.
        /// </summary>
        public bool SuppressOnMiss => suppressOnMiss;
    }
}
