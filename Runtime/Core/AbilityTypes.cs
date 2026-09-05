using System;

namespace Fofuxo.GameplayAbilitySystem
{
    public enum AbilityPhase
    {
        Startup,
        Active,
        Recovery
    }

    public enum AbilityCooldownStartPolicy
    {
        OnActivation,
        OnCompletion
    }

    public enum AbilityCancelReason
    {
        Manual,
        Roll,
        Block,
        Parried,
        HitReaction,
        Stagger,
        Knockdown,
        Death,
        TargetLost
    }

    [Flags]
    public enum AbilityCancelMask
    {
        None = 0,
        Manual = 1 << 0,
        Roll = 1 << 1,
        Block = 1 << 2,
        Parried = 1 << 3,
        HitReaction = 1 << 4,
        Stagger = 1 << 5,
        Knockdown = 1 << 6,
        Death = 1 << 7,
        TargetLost = 1 << 8,
        All = ~0
    }
}
