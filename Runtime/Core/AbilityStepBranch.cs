using System;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// Where a step sends the activation when it advances, instead of straight
    /// into the following step. The first branch whose tag the owner holds wins;
    /// with no branch, or none matching, the activation moves to the next step
    /// as it always did.
    ///
    /// The tag is read off the owner exactly like an activation's Required Tags,
    /// so a branch is authored against the same state a game already publishes —
    /// a charged stance, a low-health phase, a buff granted by an earlier step.
    /// </summary>
    [Serializable]
    public struct AbilityStepBranch
    {
        [Tooltip("Tag the owner must hold for this branch to be taken. An empty tag never matches.")]
        [SerializeField] private GameplayTag requiredTag;
        [Tooltip("Zero-based step this branch jumps to. It may point backwards, which is how a step loops.")]
        [SerializeField, Min(0)] private int targetStepIndex;

        public AbilityStepBranch(GameplayTag requiredTag, int targetStepIndex)
        {
            this.requiredTag = requiredTag;
            this.targetStepIndex = targetStepIndex;
        }

        public GameplayTag RequiredTag => requiredTag;
        public int TargetStepIndex => Mathf.Max(0, targetStepIndex);

        /// <summary>A branch with no tag is authoring debris and never fires.</summary>
        public bool IsValid => !requiredTag.IsEmpty;

        public override string ToString() =>
            $"{requiredTag} -> step {TargetStepIndex + 1}";
    }
}
