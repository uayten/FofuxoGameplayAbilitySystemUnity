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
    /// which is the one owner of an effect lifecycle. Regeneration stays here,
    /// because it is authored on the set itself and belongs to no application.
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
            /// <summary>Which attribute this row sets up.</summary>
            public GameplayAttribute Attribute => attribute;
            /// <summary>The value the actor starts with.</summary>
            public float BaseValue => baseValue;
            /// <summary>Lowest value the attribute is clamped to.</summary>
            public float MinValue => minValue;
            /// <summary>Highest value the attribute is clamped to.</summary>
            public float MaxValue => maxValue;
        }
        /// <summary>
        /// An attribute that refills by itself, in units per second.
        /// </summary>
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
            /// <summary>Which attribute refills by itself.</summary>
            public GameplayAttribute Attribute => attribute;
            /// <summary>Units added per second.</summary>
            public float PerSecond => perSecond;
        }

        [Tooltip("Shared savable defaults. When assigned, Rebuild uses it instead of the local arrays.")]
        [SerializeField] private AttributeSetDefinition definition;
        [SerializeField] private InitialValue[] initialValues = { };
        [SerializeField] private Regeneration[] regeneration = { };

        private readonly Dictionary<GameplayAttribute, AttributeValue> values = new();
        /// <summary>Attribute each issued slot belongs to, so removal finds it in one lookup.</summary>
        private readonly Dictionary<int, GameplayAttribute> slotOwners = new();
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

        private void Update()
        {
            Tick(Time.deltaTime);
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
            InitialValue[] initials = definition != null ? definition.InitialValues : initialValues;
            foreach (InitialValue initial in initials)
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
        /// <summary>The aggregate value: the base plus every live modifier.</summary>
        public float GetCurrent(GameplayAttribute attribute)
        {
            return GetOrCreate(attribute).CurrentValue;
        }
        /// <summary>The base value alone, with no modifier applied.</summary>
        public float GetBase(GameplayAttribute attribute)
        {
            return GetOrCreate(attribute).BaseValue;
        }

        /// <summary>
        /// Every attribute this set holds a value for, with its live aggregate.
        /// A read-only view for tooling; writes go through the modifier calls.
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
        /// </summary>
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
            float oldValue = value.CurrentValue;
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
            float oldValue = value.CurrentValue;
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
            float oldValue = value.CurrentValue;
            if (!value.RemoveModifier(slot))
            {
                return false;
            }

            EmitIfChanged(attribute, null, oldValue);
            return true;
        }

        /// <summary>
        /// Advances regeneration. Called automatically; public so tests can step
        /// time deterministically. Effect durations and periods are aged by
        /// <see cref="GameplayEffectContainer.Tick"/>, not here.
        /// </summary>
        public void Tick(float deltaTime)
        {
            float step = Mathf.Max(0f, deltaTime);
            if (step <= 0f)
            {
                return;
            }

            Regeneration[] regen = definition != null ? definition.Regeneration : regeneration;
            foreach (Regeneration entry in regen)
            {
                if (entry.Attribute.IsEmpty || Mathf.Approximately(entry.PerSecond, 0f))
                {
                    continue;
                }

                ApplyInstantModifier(new AttributeModifier(
                    entry.Attribute, AttributeOperation.Add, entry.PerSecond * step));
            }
        }

        private void EmitIfChanged(
            GameplayAttribute attribute,
            UnityEngine.Object source,
            float oldValue)
        {
            AttributeValue value = GetOrCreate(attribute);
            float after = value.CurrentValue;
            if (!Mathf.Approximately(oldValue, after))
            {
                Changed?.Invoke(new AttributeValueChanged(attribute, oldValue, after, source));
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
