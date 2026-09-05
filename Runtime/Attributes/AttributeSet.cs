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

        [SerializeField] private InitialValue[] initialValues = { };

        private readonly Dictionary<GameplayAttribute, AttributeValue> values = new();

        public event Action<AttributeValueChanged> Changed;

        protected virtual void Awake()
        {
            Rebuild();
        }

        /// <summary>
        /// Rebuilds runtime values from the authored initials. Used at startup
        /// and by tests that configure initials after construction.
        /// </summary>
        public void Rebuild()
        {
            values.Clear();
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
