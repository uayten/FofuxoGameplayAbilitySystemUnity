using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// Per-actor runtime value for one attribute. Base value, limits, and the
    /// attached modifiers aggregate deterministically into
    /// <see cref="CurrentValue"/>.
    ///
    /// Every attached modifier carries a slot id. Two identical modifiers from
    /// the same source are two slots, so detaching one never detaches the other
    /// by accident — which is what removal by value could not promise.
    /// </summary>
    [Serializable]
    public sealed class AttributeValue
    {
        [SerializeField] private float baseValue;
        [SerializeField] private float minValue;
        [SerializeField] private float maxValue = float.PositiveInfinity;

        private readonly List<ModifierSlot> modifiers = new();

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
        public int ModifierCount => modifiers.Count;

        public AttributeModifier GetModifier(int index) => modifiers[index].Modifier;

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
                    AttributeModifier modifier = modifiers[i].Modifier;
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

        /// <summary>Attaches a modifier under a slot id the caller owns.</summary>
        internal void AddModifier(int slot, AttributeModifier modifier)
        {
            modifiers.Add(new ModifierSlot(slot, modifier));
        }

        /// <summary>
        /// Attaches a modifier and issues its slot id. The overload tests and
        /// direct callers use; the effect layer supplies its own ids.
        /// </summary>
        public int AddModifier(AttributeModifier modifier)
        {
            int slot = NextLocalSlot();
            modifiers.Add(new ModifierSlot(slot, modifier));
            return slot;
        }

        /// <summary>Replaces the modifier in a slot, for a live magnitude that moved.</summary>
        internal bool UpdateModifier(int slot, AttributeModifier modifier)
        {
            for (int i = 0; i < modifiers.Count; i++)
            {
                if (modifiers[i].Id == slot)
                {
                    modifiers[i] = new ModifierSlot(slot, modifier);
                    return true;
                }
            }

            return false;
        }

        public bool RemoveModifier(int slot)
        {
            for (int i = 0; i < modifiers.Count; i++)
            {
                if (modifiers[i].Id == slot)
                {
                    modifiers.RemoveAt(i);
                    return true;
                }
            }

            return false;
        }

        internal void ClearModifiers()
        {
            modifiers.Clear();
        }

        /// <summary>
        /// Slot ids for modifiers attached without the effect layer. Negative so
        /// they can never collide with the positive ids the set issues.
        /// </summary>
        private int NextLocalSlot()
        {
            int slot = -1;
            for (int i = 0; i < modifiers.Count; i++)
            {
                if (modifiers[i].Id <= slot)
                {
                    slot = modifiers[i].Id - 1;
                }
            }

            return slot;
        }

        private readonly struct ModifierSlot
        {
            public ModifierSlot(int id, AttributeModifier modifier)
            {
                Id = id;
                Modifier = modifier;
            }

            public int Id { get; }
            public AttributeModifier Modifier { get; }
        }
    }
}
