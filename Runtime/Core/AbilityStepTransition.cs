namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// The last thing that moved an activation between steps, or ended it.
    /// A debugger, a combat log, or an AI that reacts to a whiffed combo reads
    /// this instead of inferring the cause from the step index changing.
    /// </summary>
    public readonly struct AbilityStepTransition
    {
        public AbilityStepTransition(
            AbilityDefinition ability,
            AbilityStepTransitionReason reason,
            int fromStepIndex,
            int toStepIndex,
            GameplayTag tag)
        {
            Ability = ability;
            Reason = reason;
            FromStepIndex = fromStepIndex;
            ToStepIndex = toStepIndex;
            Tag = tag;
        }

        /// <summary>The ability the transition belongs to.</summary>
        public AbilityDefinition Ability { get; }

        public AbilityStepTransitionReason Reason { get; }

        /// <summary>The step the activation left. Equal to <see cref="ToStepIndex"/> when it did not move.</summary>
        public int FromStepIndex { get; }

        /// <summary>The step the activation entered, or the one it ended on.</summary>
        public int ToStepIndex { get; }

        /// <summary>
        /// The cancel or event tag behind the transition, empty when the reason
        /// carries no tag.
        /// </summary>
        public GameplayTag Tag { get; }

        public bool ChangedStep => ToStepIndex != FromStepIndex;

        public override string ToString()
        {
            string abilityName = Ability == null ? "<none>" : Ability.AbilityId;
            string move = ChangedStep
                ? $"step {FromStepIndex + 1} -> {ToStepIndex + 1}"
                : $"step {ToStepIndex + 1}";
            return Tag.IsEmpty
                ? $"{abilityName}: {Reason} ({move})"
                : $"{abilityName}: {Reason} ({move}, {Tag})";
        }
    }
}
