using System.Collections.Generic;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// One duration or infinite effect while it is live on an actor. It owns
    /// the runtime state an application accumulates — remaining time, stacks,
    /// the period accumulator, the attribute modifier slots it attached and the
    /// tags it granted — and it is named by a <see cref="GameplayEffectHandle"/>
    /// that stays answerable after it is gone.
    ///
    /// Nothing here lives on the definition asset. Removing this object detaches
    /// every modifier it attached and releases every tag it granted.
    /// </summary>
    public sealed class ActiveGameplayEffect
    {
        /// <summary>One attribute modifier slot per stack, in application order.</summary>
        internal readonly List<int> modifierSlots = new();
        internal readonly List<GameplayTag> grantedTags = new();

        internal ActiveGameplayEffect(GameplayEffectHandle handle, GameplayEffectSpec spec)
        {
            Handle = handle;
            Spec = spec;
            StackCount = 1;
            TotalDuration = spec.Duration;
            RemainingDuration = spec.Duration;
            Period = spec.Period;
            PeriodAccumulator = 0f;
        }

        public GameplayEffectHandle Handle { get; }
        public GameplayEffectSpec Spec { get; private set; }

        /// <summary>
        /// The persistent cue this effect added on the target, refreshed when
        /// it stacks and removed when it ends. None when the effect authored no
        /// cue, the target has no ability system, or a filter suppressed it.
        /// </summary>
        public GameplayCueHandle CueHandle { get; internal set; }
        public GameplayEffectDefinition Definition => Spec.Definition;
        public GameObject Source => Spec.Source;
        public GameObject Target => Spec.Target;
        public int Level => Spec.Level;

        public int StackCount { get; internal set; }
        public float TotalDuration { get; internal set; }
        public float RemainingDuration { get; internal set; }
        public float Period { get; internal set; }
        public float PeriodAccumulator { get; internal set; }

        /// <summary>Number of periodic executions this effect has run.</summary>
        public int PeriodCount { get; internal set; }

        public bool IsInfinite => float.IsPositiveInfinity(RemainingDuration);
        public bool IsPeriodic => Period > 0f;
        public bool IsExpired => !IsInfinite && RemainingDuration <= 0f;

        /// <summary>
        /// Fraction of the duration already elapsed, 0 to 1. Always zero for an
        /// infinite effect, which has no end to be a fraction of.
        /// </summary>
        public float ElapsedFraction =>
            IsInfinite || TotalDuration <= 0f
                ? 0f
                : Mathf.Clamp01(1f - RemainingDuration / TotalDuration);

        /// <summary>
        /// Replaces the application data with a newer one. The most recent
        /// application defines the effect — its magnitudes, its duration, its
        /// period — and the stack count is the only thing carried across, which
        /// is what makes a refreshed stun last as long as the hit that refreshed
        /// it rather than as long as the first one.
        /// </summary>
        internal void AdoptSpec(GameplayEffectSpec spec)
        {
            Spec = spec;
            Period = spec.Period;
        }

        internal void RefreshDuration()
        {
            TotalDuration = Spec.Duration;
            RemainingDuration = Spec.Duration;
        }

        public override string ToString()
        {
            string name = Definition != null ? Definition.name : "dynamic";
            return $"{Handle} {name} x{StackCount}";
        }
    }
}
