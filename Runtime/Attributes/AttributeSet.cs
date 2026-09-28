using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// One attribute value changing: what it was, what it became, and the
    /// object that caused it.
    /// </summary>
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
        /// <summary>The attribute that changed.</summary>
        public GameplayAttribute Attribute { get; }
        /// <summary>Its aggregate value before the change.</summary>
        public float OldValue { get; }
        /// <summary>Its aggregate value after it.</summary>
        public float NewValue { get; }
        /// <summary>
        /// Whatever caused the change - the attacker, the effect's source, or
        /// null.
        /// </summary>
        public UnityEngine.Object Source { get; }
    }

    /// <summary>
    /// Owns per-actor runtime attribute values. Games subclass this with
    /// concrete sets (Health, Stamina, Poise).
    ///
    /// The set aggregates; it does not decide lifetimes. An instant change
    /// folds into the base value, and a modifier attached here stays attached
    /// until its slot is removed. How long that is — duration, period, stacking,
    /// overflow, immunity — belongs to <see cref="GameplayEffectContainer"/>,
    /// which is the one owner of an effect lifecycle. Regeneration is one of
    /// those lifetimes: an infinite periodic effect, usually granted by the
    /// actor's loadout, switched off by its Ongoing Tag Requirements.
    /// </summary>
    [DisallowMultipleComponent]
    public class AttributeSet : MonoBehaviour
    {
        /// <summary>
        /// An attribute's starting value and the limits it is clamped to.
        /// </summary>
        [Serializable]
        public struct InitialValue
        {
            [SerializeField] private GameplayAttribute attribute;
            [SerializeField] private float baseValue;
            [SerializeField] private float minValue;
            [SerializeField] private float maxValue;
            [Tooltip("Another attribute whose current value is this one's ceiling, like Max Stamina for Stamina. When set, Max Value is ignored.")]
            [SerializeField] private GameplayAttribute maxAttribute;

            public InitialValue(
                GameplayAttribute attribute,
                float baseValue,
                float minValue,
                float maxValue,
                GameplayAttribute maxAttribute = default)
            {
                this.attribute = attribute;
                this.baseValue = baseValue;
                this.minValue = minValue;
                this.maxValue = maxValue;
                this.maxAttribute = maxAttribute;
            }
            /// <summary>Which attribute this row sets up.</summary>
            public GameplayAttribute Attribute => attribute;
            /// <summary>The value the actor starts with.</summary>
            public float BaseValue => baseValue;
            /// <summary>Lowest value the attribute is clamped to.</summary>
            public float MinValue => minValue;
            /// <summary>
            /// Highest value the attribute is clamped to, unless
            /// <see cref="MaxAttribute"/> is set.
            /// </summary>
            public float MaxValue => maxValue;
            /// <summary>
            /// The attribute whose current value caps this one. Empty means the
            /// fixed <see cref="MaxValue"/> applies.
            /// </summary>
            public GameplayAttribute MaxAttribute => maxAttribute;
        }

        [Tooltip("Shared savable defaults. When assigned, Rebuild uses it instead of the local arrays.")]
        [SerializeField] private AttributeSetDefinition definition;
        [SerializeField] private InitialValue[] initialValues = { };

        private readonly Dictionary<GameplayAttribute, AttributeValue> values = new();
        /// <summary>Attribute each issued slot belongs to, so removal finds it in one lookup.</summary>
        private readonly Dictionary<int, GameplayAttribute> slotOwners = new();
        /// <summary>Capped attribute, keyed to the attribute whose current value is its ceiling.</summary>
        private readonly Dictionary<GameplayAttribute, GameplayAttribute> caps = new();
        private int nextSlot;
        private bool initialized;
        /// <summary>
        /// Raised whenever an attribute's aggregate value moves, with the old
        /// value, the new one and the source.
        /// </summary>
        public event Action<AttributeValueChanged> Changed;

        protected virtual void Awake()
        {
            EnsureInitialized();
        }

        /// <summary>
        /// Points this set at shared savable defaults and rebuilds immediately.
        /// </summary>
        public void SetDefinition(AttributeSetDefinition setDefinition)
        {
            definition = setDefinition;
            Rebuild();
        }

        /// <summary>
        /// Rebuilds runtime values from the authored initials. Used at startup
        /// and by tests that configure initials after construction.
        /// </summary>
        public void Rebuild()
        {
            initialized = true;
            values.Clear();
            slotOwners.Clear();
            caps.Clear();
            InitialValue[] initials = definition != null ? definition.InitialValues : initialValues;
            foreach (InitialValue initial in initials)
            {
                if (initial.Attribute.IsEmpty || values.ContainsKey(initial.Attribute))
                {
                    continue;
                }

                bool capped = !initial.MaxAttribute.IsEmpty && initial.MaxAttribute != initial.Attribute;
                if (capped)
                {
                    caps.Add(initial.Attribute, initial.MaxAttribute);
                }

                values.Add(
                    initial.Attribute,
                    new AttributeValue(
                        initial.BaseValue,
                        initial.MinValue,
                        capped
                            ? float.PositiveInfinity
                            : Mathf.Max(initial.MinValue, initial.MaxValue)));
            }

            DropChainedCaps();
        }

        /// <summary>
        /// The aggregate value: the base plus every live modifier, held under
        /// the Max Attribute's current value when the attribute has one.
        /// </summary>
        public float GetCurrent(GameplayAttribute attribute)
        {
            return CurrentOf(attribute, GetOrCreate(attribute));
        }

        /// <summary>
        /// The lowest value the attribute is clamped to — what a cost check
        /// compares against, the way Unreal's refuses a cost that would take an
        /// attribute below zero.
        /// </summary>
        public float GetMinimum(GameplayAttribute attribute)
        {
            return GetOrCreate(attribute).MinValue;
        }

        /// <summary>The base value alone, with no modifier applied.</summary>
        public float GetBase(GameplayAttribute attribute)
        {
            return GetOrCreate(attribute).BaseValue;
        }

        /// <summary>
        /// Every attribute this set holds a value for, with its live aggregate.
        /// A read-only view for tooling; writes go through the modifier calls.
        /// The ceiling of a Max Attribute is not applied here: read the capped
        /// value through <see cref="GetCurrent"/>.
        /// </summary>
        public IReadOnlyDictionary<GameplayAttribute, AttributeValue> Values
        {
            get
            {
                EnsureInitialized();
                return values;
            }
        }

        /// <summary>
        /// Folds a change into the base value, permanently. This is what an
        /// instant effect and every period of a periodic one do.
        ///
        /// Under a Max Attribute, Add and Multiply never leave the base above
        /// the ceiling, so a refill stops at it and a drain starts from it.
        /// Override is written as given: it is how a save restores a base
        /// whose ceiling only comes back with the effects restored after it.
        /// </summary>
        public void ApplyInstantModifier(AttributeModifier modifier)
        {
            if (modifier.Attribute.IsEmpty)
            {
                return;
            }

            AttributeValue value = GetOrCreate(modifier.Attribute);
            float oldValue = CurrentOf(modifier.Attribute, value);
            float ceiling = CeilingOf(modifier.Attribute);
            bool settlesToCeiling =
                modifier.Operation != AttributeOperation.Override &&
                !float.IsPositiveInfinity(ceiling);
            if (settlesToCeiling && value.BaseValue > ceiling)
            {
                value.SetBase(ceiling);
            }

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

            if (settlesToCeiling && value.BaseValue > ceiling)
            {
                value.SetBase(ceiling);
            }

            EmitIfChanged(modifier.Attribute, modifier.Source, oldValue);
        }
        /// <summary>
        /// Replaces the authored starting values and rebuilds the set, which
        /// clears the runtime values and every modifier slot with them.
        /// </summary>
        public void SetInitialValues(InitialValue[] initials)
        {
            initialValues = initials ?? Array.Empty<InitialValue>();
            Rebuild();
        }

        /// <summary>
        /// Attaches a modifier that contributes to the current value until its
        /// slot is removed. The returned slot id is the only way to detach this
        /// exact modifier, which is what lets two identical applications from
        /// one source coexist.
        /// </summary>
        /// <returns>The slot id, or zero when the attribute is empty.</returns>
        public int AddModifier(AttributeModifier modifier)
        {
            if (modifier.Attribute.IsEmpty)
            {
                return 0;
            }

            AttributeValue value = GetOrCreate(modifier.Attribute);
            float oldValue = CurrentOf(modifier.Attribute, value);
            nextSlot++;
            value.AddModifier(nextSlot, modifier);
            slotOwners[nextSlot] = modifier.Attribute;
            EmitIfChanged(modifier.Attribute, modifier.Source, oldValue);
            return nextSlot;
        }

        /// <summary>
        /// Replaces the modifier in a slot, for a magnitude that was recalculated
        /// while the effect stayed active.
        /// </summary>
        public bool UpdateModifier(int slot, AttributeModifier modifier)
        {
            if (!slotOwners.TryGetValue(slot, out GameplayAttribute attribute) ||
                attribute != modifier.Attribute)
            {
                return false;
            }

            AttributeValue value = GetOrCreate(attribute);
            float oldValue = CurrentOf(attribute, value);
            if (!value.UpdateModifier(slot, modifier))
            {
                return false;
            }

            EmitIfChanged(attribute, modifier.Source, oldValue);
            return true;
        }

        /// <summary>Detaches the modifier in a slot. False when it was already gone.</summary>
        public bool RemoveModifier(int slot)
        {
            if (!slotOwners.TryGetValue(slot, out GameplayAttribute attribute))
            {
                return false;
            }

            slotOwners.Remove(slot);
            AttributeValue value = GetOrCreate(attribute);
            float oldValue = CurrentOf(attribute, value);
            if (!value.RemoveModifier(slot))
            {
                return false;
            }

            EmitIfChanged(attribute, null, oldValue);
            return true;
        }


        private void EmitIfChanged(
            GameplayAttribute attribute,
            UnityEngine.Object source,
            float oldValue)
        {
            AttributeValue value = GetOrCreate(attribute);
            float after = CurrentOf(attribute, value);
            if (Mathf.Approximately(oldValue, after))
            {
                return;
            }

            Changed?.Invoke(new AttributeValueChanged(attribute, oldValue, after, source));
            EmitCappedChanges(attribute, oldValue, after, source);
        }

        /// <summary>
        /// A ceiling that moved moves every attribute it caps. Their bases are
        /// left alone, so a ceiling that drops and comes back restores them.
        /// </summary>
        private void EmitCappedChanges(
            GameplayAttribute ceilingAttribute,
            float oldCeiling,
            float newCeiling,
            UnityEngine.Object source)
        {
            if (caps.Count == 0)
            {
                return;
            }

            foreach (KeyValuePair<GameplayAttribute, GameplayAttribute> cap in caps)
            {
                if (cap.Value != ceilingAttribute)
                {
                    continue;
                }

                AttributeValue capped = GetOrCreate(cap.Key);
                float before = Clamp(capped, oldCeiling);
                float after = Clamp(capped, newCeiling);
                if (Mathf.Approximately(before, after))
                {
                    continue;
                }

                Changed?.Invoke(new AttributeValueChanged(cap.Key, before, after, source));
            }
        }

        private float CurrentOf(GameplayAttribute attribute, AttributeValue value)
        {
            return caps.Count == 0 ? value.CurrentValue : Clamp(value, CeilingOf(attribute));
        }

        private float CeilingOf(GameplayAttribute attribute)
        {
            return caps.TryGetValue(attribute, out GameplayAttribute ceiling)
                ? GetOrCreate(ceiling).CurrentValue
                : float.PositiveInfinity;
        }

        private static float Clamp(AttributeValue value, float ceiling)
        {
            return Mathf.Max(value.MinValue, Mathf.Min(value.CurrentValue, ceiling));
        }

        /// <summary>
        /// A ceiling must be a plain attribute: one that is capped itself would
        /// make every read walk a chain, and a loop would never end.
        /// </summary>
        private void DropChainedCaps()
        {
            if (caps.Count == 0)
            {
                return;
            }

            List<GameplayAttribute> chained = null;
            foreach (KeyValuePair<GameplayAttribute, GameplayAttribute> cap in caps)
            {
                if (caps.ContainsKey(cap.Value))
                {
                    (chained ??= new List<GameplayAttribute>()).Add(cap.Key);
                }
            }

            if (chained == null)
            {
                return;
            }

            foreach (GameplayAttribute attribute in chained)
            {
                Debug.LogWarning(
                    $"Attribute '{attribute}' is capped by '{caps[attribute]}', which is " +
                    "capped itself. A Max Attribute must be uncapped, so this cap is ignored.",
                    this);
                caps.Remove(attribute);
            }
        }

        /// <summary>
        /// Builds the authored initials on first access. Unity does not
        /// guarantee the Awake order of components sharing a GameObject, so a
        /// consumer reading attributes from its own Awake would otherwise see
        /// an empty set.
        /// </summary>
        private void EnsureInitialized()
        {
            if (initialized)
            {
                return;
            }

            Rebuild();
        }

        private AttributeValue GetOrCreate(GameplayAttribute attribute)
        {
            EnsureInitialized();

            if (!values.TryGetValue(attribute, out AttributeValue value))
            {
                value = new AttributeValue(0f, 0f, float.PositiveInfinity);
                values.Add(attribute, value);
            }

            return value;
        }
    }
}
