using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// Per-actor runtime value for one attribute. Base value, limits, and the
    /// listed duration modifiers aggregate deterministically into
    /// <see cref="CurrentValue"/>.
    /// </summary>
    [Serializable]
    public sealed class AttributeValue
    {
        [SerializeField] private float baseValue;
        [SerializeField] private float minValue;
        [SerializeField] private float maxValue = float.PositiveInfinity;

        private readonly List<AttributeModifier> modifiers = new();

        public AttributeValue()
        {
        }

        public AttributeValue(float baseValue, float minValue, float maxValue)
        {
            this.minValue = Mathf.Min(minValue, maxValue);
            this.maxValue = Mathf.Max(minValue, maxValue);
            this.baseValue = Mathf.Clamp(baseValue, this.minValue, this.maxValue);
        }

        public float BaseValue => baseValue;
        public float MinValue => minValue;
        public float MaxValue => maxValue;
        public IReadOnlyList<AttributeModifier> Modifiers => modifiers;

        public float CurrentValue
        {
            get
            {
                float value = Mathf.Clamp(baseValue, minValue, maxValue);
                float factor = 1f;
                bool hasOverride = false;
                float overrideValue = 0f;

                for (int i = 0; i < modifiers.Count; i++)
                {
                    AttributeModifier modifier = modifiers[i];
                    switch (modifier.Operation)
                    {
                        case AttributeOperation.Add:
                            value += modifier.Magnitude;
                            break;
                        case AttributeOperation.Multiply:
                            factor *= 1f + modifier.Magnitude;
                            break;
                        case AttributeOperation.Override:
                            hasOverride = true;
                            overrideValue = modifier.Magnitude;
                            break;
                    }
                }

                value *= factor;
                if (hasOverride)
                {
                    value = overrideValue;
                }

                return Mathf.Clamp(value, minValue, maxValue);
            }
        }

        internal void SetBase(float value)
        {
            baseValue = Mathf.Clamp(value, minValue, maxValue);
        }

        internal void Rescale(float baseValue, float minValue, float maxValue)
        {
            this.minValue = Mathf.Min(minValue, maxValue);
            this.maxValue = Mathf.Max(minValue, maxValue);
            SetBase(baseValue);
        }

        public void AddModifier(AttributeModifier modifier)
        {
            modifiers.Add(modifier);
        }

        public bool RemoveModifier(AttributeModifier modifier)
        {
            return modifiers.Remove(modifier);
        }
    }
}
