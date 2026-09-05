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
        [SerializeField, Min(1)] private int frame;
        [SerializeField] private GameplayTag cue;

        public int Frame => Mathf.Max(1, frame);
        public GameplayTag Cue => cue;
    }
}
