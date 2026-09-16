namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// One request to carry an activation into its next step, and what became
    /// of it. The system keeps the most recent one per activation even after it
    /// resolves, so a caller that pressed a button can always find out whether
    /// the press advanced a step, expired, or was refused for arriving late —
    /// which is the whole point of buffering an input in frames.
    /// </summary>
    public readonly struct AbilityStepIntent
    {
        public AbilityStepIntent(
            AbilityStepIntentSource source,
            AbilityStepIntentStatus status,
            int queuedFrame,
            int deadlineFrame,
            bool isLate)
        {
            Source = source;
            Status = status;
            QueuedFrame = queuedFrame;
            DeadlineFrame = deadlineFrame;
            IsLate = isLate;
        }

        /// <summary>What produced the request.</summary>
        public AbilityStepIntentSource Source { get; }

        /// <summary>Where the request got to. <see cref="AbilityStepIntentStatus.None"/> when there is none.</summary>
        public AbilityStepIntentStatus Status { get; }

        /// <summary>The step frame the request arrived on, 1-based.</summary>
        public int QueuedFrame { get; }

        /// <summary>
        /// The last step frame that would still have accepted it, late grace
        /// included. Zero when the step accepts input until it completes.
        /// </summary>
        public int DeadlineFrame { get; }

        /// <summary>
        /// True when the request arrived after the step's Combo Input End Frame
        /// and was only accepted by the late grace window.
        /// </summary>
        public bool IsLate { get; }

        /// <summary>True while the request is still waiting to be consumed.</summary>
        public bool IsPending => Status == AbilityStepIntentStatus.Queued;

        public static AbilityStepIntent None => default;

        /// <summary>The same request in a terminal state, for the record kept after it resolves.</summary>
        public AbilityStepIntent WithStatus(AbilityStepIntentStatus status) =>
            new(Source, status, QueuedFrame, DeadlineFrame, IsLate);

        public override string ToString() =>
            Status == AbilityStepIntentStatus.None
                ? "no step intent"
                : $"{Source} {Status} (frame {QueuedFrame}, deadline {DeadlineFrame})" +
                  (IsLate ? " late" : string.Empty);
    }
}
