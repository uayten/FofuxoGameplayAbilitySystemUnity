using System.Collections.Generic;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// One application of one effect. The definition is shared, immutable
    /// authoring data; everything that varies per use lives here — source,
    /// target, level, the attributes captured at application, the magnitudes
    /// calculated from them, the tags this application grants, and the target
    /// data it landed on.
    ///
    /// A spec is created per target, per application. Two enemies hit by the
    /// same swing get two specs off one definition, and nothing an application
    /// computes is ever written back into the asset.
    /// </summary>
    public sealed class GameplayEffectSpec
    {
        private readonly List<float> modifierMagnitudes = new();
        private readonly List<GameplayEffectModifier> dynamicModifiers = new();
        private readonly List<GameplayTag> grantedTags = new();
        private readonly Dictionary<CaptureKey, float> snapshots = new();
        private Dictionary<GameplayTag, float> setByCallerMagnitudes;

        private float durationOverride = -1f;
        private float periodOverride = -1f;

        public GameplayEffectSpec(
            GameplayEffectDefinition definition,
            GameObject source,
            GameObject target,
            int level = 1)
        {
            Definition = definition;
            Source = source;
            Target = target;
            Level = Mathf.Clamp(level <= 0 ? 1 : level, 1, 100);
            TriggerIndex = -1;
            Context = new AbilityContext(
                source,
                target,
                source != null ? source.transform.forward : Vector3.forward,
                target != null
                    ? target.transform.position
                    : source != null
                        ? source.transform.position
                        : Vector3.zero);
        }

        internal GameplayEffectSpec(
            GameplayEffectDefinition definition,
            in GameplayEffectContext context,
            GameObject target,
            in AbilityTargetHit hit)
        {
            Definition = definition;
            Source = context.Owner;
            SourceSystem = context.AbilitySystem;
            SourceInstance = context.Instance;
            SourceAbility = context.Definition;
            TriggerIndex = context.TriggerIndex;
            Level = context.Level;
            Target = target;
            Context = context.AbilityContext;
            Hit = hit;
            HasHit = hit.IsValid;
        }

        /// <summary>The shared authoring asset. Never written to.</summary>
        public GameplayEffectDefinition Definition { get; }

        /// <summary>The actor that applied the effect.</summary>
        public GameObject Source { get; }

        /// <summary>The actor the effect landed on.</summary>
        public GameObject Target { get; }

        /// <summary>The source ability system, when an ability applied this.</summary>
        public AbilitySystem SourceSystem { get; }

        /// <summary>The activation that applied this, or null outside an ability timeline.</summary>
        public AbilityInstance SourceInstance { get; }

        /// <summary>The ability that owns the trigger, when there was one.</summary>
        public AbilityDefinition SourceAbility { get; }

        /// <summary>Index of the step effect trigger that fired, or -1.</summary>
        public int TriggerIndex { get; }

        /// <summary>Magnitude level, 1 to 100.</summary>
        public int Level { get; }

        /// <summary>The activation context the effect was applied from.</summary>
        public AbilityContext Context { get; }

        /// <summary>The query hit this spec landed on, when a shape produced it.</summary>
        public AbilityTargetHit Hit { get; }

        public bool HasHit { get; }

        /// <summary>
        /// The push this application carries, as its effect authored it against
        /// where the target stands now. What a hit-reaction ability runs as its
        /// own movement; false for an effect that moves nobody.
        /// </summary>
        public bool TryGetKnockback(out Vector3 velocity, out float duration)
        {
            if (Definition != null)
            {
                return Definition.TryGetKnockback(this, out velocity, out duration);
            }

            velocity = Vector3.zero;
            duration = 0f;
            return false;
        }

        /// <summary>
        /// The calculated magnitude of every modifier the definition declares,
        /// followed by every dynamic modifier this spec added. Filled by
        /// <see cref="CalculateMagnitudes"/> at application.
        /// </summary>
        public IReadOnlyList<float> ModifierMagnitudes => modifierMagnitudes;

        /// <summary>Modifiers this application added on top of the definition.</summary>
        public IReadOnlyList<GameplayEffectModifier> DynamicModifiers => dynamicModifiers;

        /// <summary>Tags this application grants beyond the ones the definition declares.</summary>
        public IReadOnlyList<GameplayTag> DynamicGrantedTags => grantedTags;

        /// <summary>Calculated seconds this application lasts. Infinity for an infinite effect.</summary>
        public float Duration { get; private set; }

        /// <summary>Calculated seconds between periodic executions. Zero for a non-periodic effect.</summary>
        public float Period { get; private set; }

        /// <summary>
        /// Overrides the authored duration for this application only. Used by
        /// code-driven effects whose duration is decided per hit.
        /// </summary>
        public GameplayEffectSpec SetDuration(float seconds)
        {
            durationOverride = Mathf.Max(0f, seconds);
            return this;
        }

        /// <summary>Overrides the authored period for this application only.</summary>
        public GameplayEffectSpec SetPeriod(float seconds)
        {
            periodOverride = Mathf.Max(0f, seconds);
            return this;
        }

        /// <summary>
        /// Adds a modifier this application carries and the asset does not.
        /// This is how code applies a timed attribute change without inventing
        /// an asset for it.
        /// </summary>
        public GameplayEffectSpec AddDynamicModifier(GameplayEffectModifier modifier)
        {
            if (!modifier.IsEmpty)
            {
                dynamicModifiers.Add(modifier);
            }

            return this;
        }

        /// <summary>
        /// Adds a modifier to this application from its three parts, for code
        /// that has no asset to author one on.
        /// </summary>
        public GameplayEffectSpec AddDynamicModifier(
            GameplayAttribute attribute, AttributeOperation operation, float magnitude)
        {
            return AddDynamicModifier(
                new GameplayEffectModifier(attribute, operation, magnitude));
        }

        /// <summary>
        /// Sets the number a Set By Caller magnitude reads under this tag, the
        /// way an ability tells its effect how much — how fast a sprint goes,
        /// how much each installment of its cost takes. Set before the spec is
        /// applied; a magnitude whose tag was never set reads zero.
        /// </summary>
        public GameplayEffectSpec SetSetByCallerMagnitude(GameplayTag tag, float magnitude)
        {
            if (!tag.IsEmpty)
            {
                (setByCallerMagnitudes ??= new Dictionary<GameplayTag, float>())[tag] = magnitude;
            }

            return this;
        }

        /// <summary>The number set under this tag, or <paramref name="fallback"/> when none was.</summary>
        public float GetSetByCallerMagnitude(GameplayTag tag, float fallback = 0f)
        {
            return setByCallerMagnitudes != null &&
                   setByCallerMagnitudes.TryGetValue(tag, out float magnitude)
                ? magnitude
                : fallback;
        }

        /// <summary>Every Set By Caller number on this spec, for persistence and tooling.</summary>
        public IReadOnlyDictionary<GameplayTag, float> SetByCallerMagnitudes =>
            setByCallerMagnitudes ??= new Dictionary<GameplayTag, float>();

        /// <summary>Grants a tag for this application only, on top of the authored ones.</summary>
        public GameplayEffectSpec AddGrantedTag(GameplayTag tag)
        {
            if (!tag.IsEmpty && !grantedTags.Contains(tag))
            {
                grantedTags.Add(tag);
            }

            return this;
        }

        /// <summary>
        /// Total number of modifiers this application carries: the definition
        /// ones first, then the dynamic ones.
        /// </summary>
        public int ModifierCount =>
            (Definition != null ? Definition.Modifiers.Count : 0) + dynamicModifiers.Count;

        /// <summary>
        /// The modifier at an index across the asset's own and this
        /// application's dynamic ones.
        /// </summary>
        public GameplayEffectModifier GetModifier(int index)
        {
            int authored = Definition != null ? Definition.Modifiers.Count : 0;
            return index < authored
                ? Definition.Modifiers[index]
                : dynamicModifiers[index - authored];
        }

        /// <summary>
        /// Reads a captured attribute under this spec capture rules. A snapshot
        /// value comes from the table filled at application; a live one is read
        /// off the actor now, so it moves while the effect is active.
        /// </summary>
        public float GetCapturedAttribute(
            GameplayEffectCaptureSource capture, GameplayAttribute attribute, bool snapshot)
        {
            if (attribute.IsEmpty)
            {
                return 0f;
            }

            if (snapshot)
            {
                return snapshots.TryGetValue(new CaptureKey(capture, attribute), out float value)
                    ? value
                    : 0f;
            }

            return ReadAttribute(capture, attribute);
        }

        /// <summary>True when the effect captured this attribute at application.</summary>
        public bool TryGetSnapshot(
            GameplayEffectCaptureSource capture, GameplayAttribute attribute, out float value)
        {
            return snapshots.TryGetValue(new CaptureKey(capture, attribute), out value);
        }

        /// <summary>
        /// Reads every attribute a snapshot magnitude needs, once. Called before
        /// the first magnitude calculation and never again, which is what makes
        /// a snapshot a snapshot.
        /// </summary>
        internal void CaptureSnapshots()
        {
            snapshots.Clear();
            for (int i = 0; i < ModifierCount; i++)
            {
                CaptureMagnitude(GetModifier(i).Magnitude);
            }

            Definition?.CaptureMagnitudes(this);
        }

        /// <summary>
        /// Records the attribute a magnitude snapshots, once. A definition
        /// declares every magnitude it owns through
        /// <c>GameplayEffectDefinition.CaptureMagnitudes</c>, so a subclass that
        /// adds an authored magnitude gets the same capture rules for free.
        /// </summary>
        internal void CaptureMagnitude(GameplayEffectMagnitude magnitude)
        {
            if (!magnitude.CapturesAttribute || !magnitude.Snapshot)
            {
                return;
            }

            CaptureKey key = new(magnitude.Capture, magnitude.Attribute);
            if (!snapshots.ContainsKey(key))
            {
                snapshots[key] = ReadAttribute(magnitude.Capture, magnitude.Attribute);
            }
        }

        /// <summary>
        /// Resolves every magnitude, the duration and the period. Runs at
        /// application, and again whenever a live magnitude has to be refreshed.
        /// </summary>
        internal void CalculateMagnitudes()
        {
            modifierMagnitudes.Clear();
            for (int i = 0; i < ModifierCount; i++)
            {
                modifierMagnitudes.Add(GetModifier(i).Magnitude.Evaluate(this));
            }

            if (Definition == null)
            {
                Duration = 0f;
                Period = 0f;
                return;
            }

            Duration = Definition.DurationPolicy switch
            {
                GameplayEffectDurationPolicy.Infinite => float.PositiveInfinity,
                GameplayEffectDurationPolicy.Duration => durationOverride >= 0f
                    ? durationOverride
                    : Mathf.Max(0f, Definition.Duration.Evaluate(this)),
                _ => 0f,
            };
            Period = periodOverride >= 0f
                ? periodOverride
                : Mathf.Max(0f, Definition.Period.Evaluate(this));
        }

        /// <summary>True when any magnitude has to be recalculated while active.</summary>
        internal bool HasLiveMagnitudes
        {
            get
            {
                for (int i = 0; i < ModifierCount; i++)
                {
                    if (GetModifier(i).Magnitude.IsLive)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        private float ReadAttribute(
            GameplayEffectCaptureSource capture, GameplayAttribute attribute)
        {
            GameObject actor = capture == GameplayEffectCaptureSource.Source ? Source : Target;
            if (actor == null)
            {
                return 0f;
            }

            AttributeSet set = actor.GetComponent<AttributeSet>();
            return set != null ? set.GetCurrent(attribute) : 0f;
        }

        private readonly struct CaptureKey : System.IEquatable<CaptureKey>
        {
            private readonly GameplayEffectCaptureSource capture;
            private readonly GameplayAttribute attribute;

            public CaptureKey(GameplayEffectCaptureSource capture, GameplayAttribute attribute)
            {
                this.capture = capture;
                this.attribute = attribute;
            }

            public bool Equals(CaptureKey other) =>
                capture == other.capture && attribute == other.attribute;

            public override bool Equals(object obj) => obj is CaptureKey other && Equals(other);

            public override int GetHashCode() =>
                ((int)capture * 397) ^ attribute.GetHashCode();
        }
    }
}
