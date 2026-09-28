using System;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// One ability-driven actor, written down. Plain serializable data with no
    /// reference to a runtime object, so a consumer may hand it to
    /// <see cref="JsonUtility"/>, to its own save system, or to neither.
    /// </summary>
    /// <remarks>
    /// An actor is captured at rest. A running activation is a timeline, an
    /// animator state and a set of live tasks, and rebuilding one from a file
    /// would be a different feature from remembering what an actor owns; what
    /// is here is the state that outlives the activation - cooldowns, charges,
    /// attributes, persistent effects and loose tags.
    /// </remarks>
    [Serializable]
    public sealed class AbilitySaveRecord
    {
        /// <summary>Schema version written by this package today.</summary>
        /// <remarks>
        /// Version 2 moved cooldowns into the effects list — a cooldown is a
        /// Cooldown Gameplay Effect — and added Set By Caller magnitudes to
        /// effect entries. A version 1 record is refused unless the game
        /// registers a migration for it.
        /// </remarks>
        public const int CurrentVersion = 2;

        [SerializeField] private int version = CurrentVersion;
        [SerializeField] private long savedAtUtcTicks;
        [SerializeField] private AbilitySaveAbilityEntry[] abilities = Array.Empty<AbilitySaveAbilityEntry>();
        [SerializeField] private AbilitySaveAttributeEntry[] attributes = Array.Empty<AbilitySaveAttributeEntry>();
        [SerializeField] private AbilitySaveEffectEntry[] effects = Array.Empty<AbilitySaveEffectEntry>();
        [SerializeField] private string[] looseTags = Array.Empty<string>();

        /// <summary>Schema version this record was written with.</summary>
        public int Version
        {
            get => version;
            internal set => version = value;
        }

        /// <summary>When the record was captured, as UTC ticks.</summary>
        public long SavedAtUtcTicks
        {
            get => savedAtUtcTicks;
            internal set => savedAtUtcTicks = value;
        }

        /// <summary>
        /// When the record was captured. This is the clock a day/night cycle or
        /// any other offline progression reads: the package never decides what
        /// elapsed real time means, it only records when the save was taken.
        /// </summary>
        public DateTime SavedAtUtc => new(savedAtUtcTicks, DateTimeKind.Utc);

        public AbilitySaveAbilityEntry[] Abilities
        {
            get => abilities ??= Array.Empty<AbilitySaveAbilityEntry>();
            internal set => abilities = value ?? Array.Empty<AbilitySaveAbilityEntry>();
        }

        public AbilitySaveAttributeEntry[] Attributes
        {
            get => attributes ??= Array.Empty<AbilitySaveAttributeEntry>();
            internal set => attributes = value ?? Array.Empty<AbilitySaveAttributeEntry>();
        }

        public AbilitySaveEffectEntry[] Effects
        {
            get => effects ??= Array.Empty<AbilitySaveEffectEntry>();
            internal set => effects = value ?? Array.Empty<AbilitySaveEffectEntry>();
        }

        public string[] LooseTags
        {
            get => looseTags ??= Array.Empty<string>();
            internal set => looseTags = value ?? Array.Empty<string>();
        }
    }

    /// <summary>What one granted ability remembers between sessions.</summary>
    [Serializable]
    public sealed class AbilitySaveAbilityEntry
    {
        [SerializeField] private string abilityId;
        [SerializeField] private float charges = -1f;
        [SerializeField] private float chargeRestoreElapsed;

        /// <summary><see cref="AbilityDefinition.AbilityId"/> of the ability.</summary>
        public string AbilityId
        {
            get => abilityId ?? string.Empty;
            set => abilityId = value;
        }

        /// <summary>Charges left, or a negative number for an ability with no charge limit.</summary>
        public float Charges
        {
            get => charges;
            set => charges = value;
        }

        /// <summary>Seconds already counted towards the next charge.</summary>
        public float ChargeRestoreElapsed
        {
            get => chargeRestoreElapsed;
            set => chargeRestoreElapsed = value;
        }

        public bool HasCharges => charges >= 0f;
    }

    /// <summary>One attribute's base value. Modifiers return with their effects.</summary>
    [Serializable]
    public sealed class AbilitySaveAttributeEntry
    {
        [SerializeField] private string attributeId;
        [SerializeField] private float baseValue;

        public string AttributeId
        {
            get => attributeId ?? string.Empty;
            set => attributeId = value;
        }

        public float BaseValue
        {
            get => baseValue;
            set => baseValue = value;
        }
    }

    /// <summary>
    /// One active gameplay effect. The source actor is deliberately absent: a
    /// GameObject is not data, and an effect restored in a later session is
    /// owned by the target that carries it.
    /// </summary>
    [Serializable]
    public sealed class AbilitySaveEffectEntry
    {
        [SerializeField] private string effectId;
        [SerializeField] private int level = 1;
        [SerializeField] private int stackCount = 1;
        [SerializeField] private bool infinite;
        [SerializeField] private float totalDuration;
        [SerializeField] private float remainingDuration;
        [SerializeField] private float period;
        [SerializeField] private float periodAccumulator;
        [SerializeField] private int periodCount;
        [SerializeField] private string[] setByCallerTags = Array.Empty<string>();
        [SerializeField] private float[] setByCallerValues = Array.Empty<float>();

        /// <summary>Tags of the Set By Caller magnitudes the application carried.</summary>
        public string[] SetByCallerTags
        {
            get => setByCallerTags ??= Array.Empty<string>();
            set => setByCallerTags = value ?? Array.Empty<string>();
        }

        /// <summary>The numbers set under <see cref="SetByCallerTags"/>, in the same order.</summary>
        public float[] SetByCallerValues
        {
            get => setByCallerValues ??= Array.Empty<float>();
            set => setByCallerValues = value ?? Array.Empty<float>();
        }

        /// <summary><see cref="GameplayEffectDefinition.EffectId"/> of the effect.</summary>
        public string EffectId
        {
            get => effectId ?? string.Empty;
            set => effectId = value;
        }

        public int Level
        {
            get => level;
            set => level = value;
        }

        public int StackCount
        {
            get => stackCount;
            set => stackCount = value;
        }

        /// <summary>True for an effect with no end, whose remaining duration means nothing.</summary>
        public bool Infinite
        {
            get => infinite;
            set => infinite = value;
        }

        public float TotalDuration
        {
            get => totalDuration;
            set => totalDuration = value;
        }

        public float RemainingDuration
        {
            get => remainingDuration;
            set => remainingDuration = value;
        }

        public float Period
        {
            get => period;
            set => period = value;
        }

        public float PeriodAccumulator
        {
            get => periodAccumulator;
            set => periodAccumulator = value;
        }

        public int PeriodCount
        {
            get => periodCount;
            set => periodCount = value;
        }
    }
}
