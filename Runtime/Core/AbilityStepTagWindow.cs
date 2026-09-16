using System;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// A gameplay tag held for part of a step instead of for the whole
    /// activation. This is where an ability's behaviour is authored: i-frames,
    /// movement locks, super armour and hyper armour are all "this tag is on
    /// between these two frames", and several tags sharing one window turn on
    /// and off together, on the same frame, by construction.
    ///
    /// <see cref="AbilityDefinition.GrantedTags"/> still covers the tags that
    /// describe the whole activation. A window covers the ones whose whole
    /// point is that they end before the animation does.
    /// </summary>
    [Serializable]
    public struct AbilityStepTagWindow : IEquatable<AbilityStepTagWindow>
    {
        [Tooltip("Tag held while the window is open. Consumers read it with AbilitySystem.HasTag.")]
        [SerializeField] private GameplayTag tag;

        [Tooltip("First 1-based frame the tag is held on.")]
        [SerializeField, Min(1)] private int startFrame;

        [Tooltip("Last 1-based frame the tag is held on. Zero holds it through the end of the step.")]
        [SerializeField, Min(0)] private int endFrame;

        public AbilityStepTagWindow(GameplayTag tag, int startFrame, int endFrame)
        {
            this.tag = tag;
            this.startFrame = startFrame;
            this.endFrame = endFrame;
        }

        public GameplayTag Tag => tag;

        public int StartFrame => Mathf.Max(1, startFrame);

        /// <summary>Zero means the window runs through the end of the step.</summary>
        public int EndFrame => endFrame <= 0 ? 0 : Mathf.Max(StartFrame, endFrame);

        public bool RunsToEndOfStep => EndFrame <= 0;

        /// <summary>
        /// Whether the tag is held on this frame. <paramref name="lastFrame"/>
        /// is the step's last frame, used only by a window that runs to the end.
        /// </summary>
        public bool Contains(int frame, int lastFrame)
        {
            if (tag.IsEmpty || frame < StartFrame)
            {
                return false;
            }

            return RunsToEndOfStep ? frame <= lastFrame : frame <= EndFrame;
        }

        public bool Equals(AbilityStepTagWindow other)
        {
            return tag.Equals(other.tag) &&
                   StartFrame == other.StartFrame &&
                   EndFrame == other.EndFrame;
        }

        public override bool Equals(object obj)
        {
            return obj is AbilityStepTagWindow other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (tag.GetHashCode() * 397 ^ StartFrame) * 397 ^ EndFrame;
        }

        public override string ToString()
        {
            return RunsToEndOfStep
                ? $"{tag} [{StartFrame}..end]"
                : $"{tag} [{StartFrame}..{EndFrame}]";
        }
    }
}
