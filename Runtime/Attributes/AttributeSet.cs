using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    public readonly struct AttributeValueChanged
    {
        public AttributeValueChanged(
            GameplayAttribute attribute,
            float oldValue,
            float newValue,
            UnityEngine.Object source)
        {
            Attribute = attribute;
            OldValue = oldValue;
            NewValue = newValue;
            Source = source;
        }

        public GameplayAttribute Attribute { get; }
        public float OldValue { get; }
        public float NewValue { get; }
        public UnityEngine.Object Source { get; }
    }

    /// <summary>
    /// Owns per-actor runtime attribute values. Games subclass this with
    /// concrete sets (Health, Stamina, Poise). Instant modifiers fold into the
    /// base value; duration support arrives through the modifiers list.
    /// </summary>
    public enum EffectStacking
    {
        Stack,
        Refresh,
        Ignore
    }

    [DisallowMultipleComponent]
    public class AttributeSet : MonoBehaviour
    {
        [Serializable]
        public struct InitialValue
        {
            [SerializeField] private GameplayAttribute attribute;
            [SerializeField] private float baseValue;
            [SerializeField] private float minValue;
            [SerializeField] private float maxValue;

            public InitialValue(
                GameplayAttribute attribute,
                float baseValue,
                float minValue,
                float maxValue)
            {
                this.attribute = attribute;
                this.baseValue = baseValue;
                this.minValue = minValue;
                this.maxValue = maxValue;
            }

            public GameplayAttribute Attribute => attribute;
            public float BaseValue => baseValue;
            public float MinValue => minValue;
            public float MaxValue => maxValue;
        }

        [Serializable]
        public struct Regeneration
        {
            [SerializeField] private GameplayAttribute attribute;
            [SerializeField] private float perSecond;

            public Regeneration(GameplayAttribute attribute, float perSecond)
            {
                this.attribute = attribute;
                this.perSecond = perSecond;
            }

            public GameplayAttribute Attribute => attribute;
            public float PerSecond => perSecond;
        }

        [SerializeField] private InitialValue[] initialValues = { };
        [SerializeField] private Regeneration[] regeneration = { };

        private readonly Dictionary<GameplayAttribute, AttributeValue> values = new();
        private readonly List<DurationEntry> durationEntries = new();

        public event Action<AttributeValueChanged> Changed;

        protected virtual void Awake()
        {
            Rebuild();
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        /// <summary>
        /// Rebuilds runtime values from the authored initials. Used at startup
        /// and by tests that configure initials after construction.
        /// </summary>
        public void Rebuild()
        {
            values.Clear();
            durationEntries.Clear();
            foreach (InitialValue initial in initialValues)
            {
                if (initial.Attribute.IsEmpty || values.ContainsKey(initial.Attribute))
                {
                    continue;
                }

                values.Add(
                    initial.Attribute,
                    new AttributeValue(
                        initial.BaseValue,
                        initial.MinValue,
                        Mathf.Max(initial.MinValue, initial.MaxValue)));
            }
        }

        public float GetCurrent(GameplayAttribute attribute)
        {
            return GetOrCreate(attribute).CurrentValue;
        }

        public float GetBase(GameplayAttribute attribute)
        {
            return GetOrCreate(attribute).BaseValue;
        }

        public void ApplyInstantModifier(AttributeModifier modifier)
        {
            if (modifier.Attribute.IsEmpty)
            {
                return;
            }

            AttributeValue value = GetOrCreate(modifier.Attribute);
            float oldValue = value.CurrentValue;
            switch (modifier.Operation)
            {
                case AttributeOperation.Add:
                    value.SetBase(value.BaseValue + modifier.Magnitude);
                    break;
                case AttributeOperation.Multiply:
                    value.SetBase(value.BaseValue * (1f + modifier.Magnitude));
                    break;
                case AttributeOperation.Override:
                    value.SetBase(modifier.Magnitude);
                    break;
            }

            float newValue = value.CurrentValue;
            if (!Mathf.Approximately(oldValue, newValue))
            {
                Changed?.Invoke(new AttributeValueChanged(
                    modifier.Attribute, oldValue, newValue, modifier.Source));
            }
        }

        public void SetInitialValues(InitialValue[] initials)
        {
            initialValues = initials ?? Array.Empty<InitialValue>();
            Rebuild();
        }

        /// <summary>
        /// Applies a duration modifier with a stacking policy. Stacking matches
        /// on attribute, operation, and source. Zero or negative durations
        /// behave as instant modifiers.
        /// </summary>
        /// <returns>True when the modifier is (or stays) applied.</returns>
        public bool ApplyDurationModifier(
            AttributeModifier modifier,
            float durationSeconds,
            EffectStacking stacking)
        {
            if (modifier.Attribute.IsEmpty)
            {
                return false;
            }

            if (durationSeconds <= 0f)
            {
                ApplyInstantModifier(modifier);
                return true;
            }

            int existing = durationEntries.FindIndex(entry =>
                entry.Modifier.Attribute == modifier.Attribute &&
                entry.Modifier.Operation == modifier.Operation &&
                entry.Modifier.Source == modifier.Source);
            if (existing >= 0)
            {
                switch (stacking)
                {
                    case EffectStacking.Ignore:
                        return true;
                    case EffectStacking.Refresh:
                        durationEntries[existing] = new DurationEntry(
                            modifier, durationSeconds);
                        EmitIfChanged(modifier.Attribute, modifier.Source);
                        return true;
                }
            }

            AttributeValue value = GetOrCreate(modifier.Attribute);
            float oldValue = value.CurrentValue;
            value.AddModifier(modifier);
            durationEntries.Add(new DurationEntry(modifier, durationSeconds));
            EmitIfChanged(modifier.Attribute, modifier.Source, oldValue);
            return true;
        }

        /// <summary>
        /// Advances regeneration and duration expiry. Called automatically;
        /// public so tests can step time deterministically.
        /// </summary>
        public void Tick(float deltaTime)
        {
            float step = Mathf.Max(0f, deltaTime);
            if (step <= 0f)
            {
                return;
            }

            foreach (Regeneration entry in regeneration)
            {
                if (entry.Attribute.IsEmpty || Mathf.Approximately(entry.PerSecond, 0f))
                {
                    continue;
                }

                ApplyInstantModifier(new AttributeModifier(
                    entry.Attribute, AttributeOperation.Add, entry.PerSecond * step));
            }

            for (int i = durationEntries.Count - 1; i >= 0; i--)
            {
                DurationEntry entry = durationEntries[i];
                float remaining = entry.Remaining - step;
                if (remaining > 0f)
                {
                    durationEntries[i] = new DurationEntry(entry.Modifier, remaining);
                    continue;
                }

                durationEntries.RemoveAt(i);
                AttributeValue value = GetOrCreate(entry.Modifier.Attribute);
                float oldValue = value.CurrentValue;
                value.RemoveModifier(entry.Modifier);
                float newValue = value.CurrentValue;
                if (!Mathf.Approximately(oldValue, newValue))
                {
                    Changed?.Invoke(new AttributeValueChanged(
                        entry.Modifier.Attribute, oldValue, newValue, entry.Modifier.Source));
                }
            }
        }

        private void EmitIfChanged(
            GameplayAttribute attribute,
            UnityEngine.Object source,
            float? oldValue = null)
        {
            AttributeValue value = GetOrCreate(attribute);
            float before = oldValue ?? value.CurrentValue;
            float after = value.CurrentValue;
            if (!Mathf.Approximately(before, after))
            {
                Changed?.Invoke(new AttributeValueChanged(attribute, before, after, source));
            }
        }

        private readonly struct DurationEntry
        {
            public DurationEntry(AttributeModifier modifier, float remaining)
            {
                Modifier = modifier;
                Remaining = remaining;
            }

            public AttributeModifier Modifier { get; }
            public float Remaining { get; }
        }

        private AttributeValue GetOrCreate(GameplayAttribute attribute)
        {
            if (!values.TryGetValue(attribute, out AttributeValue value))
            {
                value = new AttributeValue(0f, 0f, float.PositiveInfinity);
                values.Add(attribute, value);
            }

            return value;
        }
    }
}
