namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// The three parts of a step's timeline: the wind-up, the frames that can
    /// hit, and the recovery that follows.
    /// </summary>
    public enum AbilityPhase
    {
        Startup,
        Active,
        Recovery
    }
    /// <summary>
    /// Whether an ability's cooldown effect is applied when it commits or only
    /// when it finishes.
    /// </summary>
    public enum AbilityCooldownStartPolicy
    {
        /// <summary>Applied with the cost, when the ability commits.</summary>
        OnCommit,
        /// <summary>Applied when a committed activation completes; a cancel starts none.</summary>
        OnCompletion
    }

    /// <summary>
    /// When an ability commits — pays its cost and starts a commit-time
    /// cooldown. Unreal's GAS leaves it to the ability's graph to call
    /// CommitAbility; a data-driven ability here commits on activation unless
    /// it asks to do it itself.
    /// </summary>
    public enum AbilityCommitPolicy
    {
        /// <summary>Commits as the activation starts, before OnActivated runs.</summary>
        OnActivation,
        /// <summary>Commits when the ability calls <c>AbilitySystem.TryCommitAbility</c>.</summary>
        Manual
    }

    /// <summary>
    /// Why an activation was refused. Ordered as the activation checks run, so
    /// the first failing check names the code. Paired with a message inside
    /// <see cref="AbilityActivationResult"/>.
    /// </summary>
    public enum AbilityActivationRejection
    {
        /// <summary>No rejection: the activation was accepted.</summary>
        None = 0,
        /// <summary>No ability was supplied.</summary>
        NullAbility,
        /// <summary>The ability asset itself fails <c>TryValidate</c>.</summary>
        InvalidDefinition,
        /// <summary>Another ability is already running on this system.</summary>
        AnotherAbilityActive,
        /// <summary>The ability is not in the system's loadout.</summary>
        NotGranted,
        /// <summary>The cooldown has not elapsed.</summary>
        OnCooldown,
        /// <summary>Every charge is spent and none has been restored yet.</summary>
        NoChargesLeft,
        /// <summary>
        /// The cost effect cannot be paid: one of its additive modifiers would
        /// take an attribute below its minimum.
        /// </summary>
        InsufficientAttribute,
        /// <summary>A tag the ability requires is not present on the owner.</summary>
        MissingRequiredTag,
        /// <summary>A tag present on the owner blocks the ability.</summary>
        BlockedByTag,
        /// <summary>The ability requires a target and the context carries none.</summary>
        MissingTarget,
        /// <summary>The target sits outside the minimum/maximum range.</summary>
        OutOfRange,
        /// <summary>The target sits outside the maximum facing angle.</summary>
        OutsideFacingAngle,
        /// <summary>
        /// A running ability from the same exclusion group blocks this one, and
        /// the group policy blocks instead of cancelling.
        /// </summary>
        BlockedByGroup,
        /// <summary>
        /// Starting would mean cancelling a running ability whose own cancel
        /// policy refuses the request.
        /// </summary>
        BlockedByUncancellableAbility,
        /// <summary>
        /// The ability moves its owner — a displacement window, or a target
        /// assist that approaches — and the owner has no <c>IAbilityMotor</c>
        /// and no Rigidbody, so the travel would reach nothing.
        /// </summary>
        MissingMotor,
        /// <summary>
        /// Every package check passed and the ability's own
        /// <c>CanActivateAbility</c> said no — a rule of the game, such
        /// as "there is something to pick up". Checked last.
        /// </summary>
        ConditionNotMet
    }

    /// <summary>
    /// Which cancel requests may interrupt an ability. Requests carry a tag, so
    /// a game adds its own reasons without editing this package: only the
    /// policy is generic, never the list of reasons.
    /// </summary>
    public enum AbilityCancelPolicy
    {
        /// <summary>Any request interrupts. The default, and what most attacks want.</summary>
        Anything,
        /// <summary>Only a request carrying one of the ability's Cancelled By Tags.</summary>
        OnlyListedTags,
        /// <summary>Nothing interrupts. The ability runs to its own end.</summary>
        Nothing
    }

    /// <summary>
    /// What a step does when it reaches the end of its timeline and nothing
    /// carried the activation into the next step. Only meaningful for the
    /// advancement modes that wait for something — an Automatic combo always
    /// chains — and only while the ability still has a next step.
    /// </summary>
    public enum AbilityStepTimeoutPolicy
    {
        /// <summary>
        /// The ability ends on the step it reached. This is what a player combo
        /// wants: no input, no next swing, and no cancellation either.
        /// </summary>
        CompleteAbility,
        /// <summary>Chain anyway, as an Automatic combo would.</summary>
        AdvanceStep,
        /// <summary>
        /// End as a cancellation carrying
        /// <see cref="CommonGameplayTags.CancelStepTimeout"/>, for a sequence
        /// whose unfinished form is a failure rather than an ending.
        /// </summary>
        CancelAbility,
        /// <summary>
        /// Stay on the final frame of the step and keep waiting. Nothing ends
        /// the ability but an advance, an explicit completion, or a
        /// cancellation, so this is the mode that needs an owner watching it.
        /// </summary>
        HoldLastStep
    }

    /// <summary>
    /// How an input maps to an activation. The package never reads a device:
    /// the policy tells whichever component owns the input — the package's own
    /// <c>AbilityInputRouter</c>, or game code — which edge to activate on.
    /// </summary>
    public enum AbilityActivationInputPolicy
    {
        /// <summary>Activate as soon as the input is pressed.</summary>
        OnPress,
        /// <summary>
        /// Activate once the input has been held for the ability's hold
        /// duration. Releasing earlier activates nothing.
        /// </summary>
        OnHold,
        /// <summary>
        /// Activate when the input is released, provided it was held for at
        /// least the ability's hold duration.
        /// </summary>
        OnRelease
    }

    /// <summary>
    /// What an activation does about the abilities already running. The default
    /// is the single-active-ability rule the system started with; every other
    /// value is scoped by the ability's Group Tag, so two abilities coexist
    /// only when an author said they may.
    /// </summary>
    public enum AbilityGroupExclusionPolicy
    {
        /// <summary>Any running ability refuses this activation. One at a time.</summary>
        BlockWhileAnyActive,
        /// <summary>
        /// Only a running ability in the same group refuses this activation.
        /// Abilities in other groups keep running alongside it.
        /// </summary>
        BlockWhileSameGroupActive,
        /// <summary>
        /// Cancel the running abilities in the same group and take their place.
        /// Rejected when one of them refuses the cancel request.
        /// </summary>
        CancelSameGroup,
        /// <summary>
        /// Cancel every running ability and take their place. Rejected when one
        /// of them refuses the cancel request.
        /// </summary>
        CancelAnyActive
    }

    /// <summary>What produced a queued request to advance a step.</summary>
    public enum AbilityStepIntentSource
    {
        /// <summary>A caller asked for the next step — typically a player input.</summary>
        Input,
        /// <summary>A gameplay event matching the ability's step advance tag.</summary>
        GameplayEvent,
        /// <summary>The ability's advance condition reported true.</summary>
        Condition
    }

    /// <summary>
    /// The fate of a queued advance request. Every accepted request reaches
    /// exactly one terminal state, which is what makes "consumed once, or
    /// expired for a reason" observable from outside the system.
    /// </summary>
    public enum AbilityStepIntentStatus
    {
        /// <summary>No request has been made on this activation yet.</summary>
        None,
        /// <summary>Accepted and still waiting for the step to open its window.</summary>
        Queued,
        /// <summary>Carried the activation into a step.</summary>
        Consumed,
        /// <summary>Accepted, then outlived the step that could have used it.</summary>
        Expired,
        /// <summary>Refused on arrival, past the step's input deadline.</summary>
        Rejected
    }

    /// <summary>
    /// Why the activation last changed step or ended. Read together with the
    /// step indices carried by <see cref="AbilityStepTransition"/>.
    /// </summary>
    public enum AbilityStepTransitionReason
    {
        /// <summary>Nothing has happened on this activation yet.</summary>
        None,
        /// <summary>The ability started on its first step.</summary>
        Activated,
        /// <summary>An Automatic combo chained on its own.</summary>
        AutomaticChain,
        /// <summary>A request queued inside the step's input window was consumed.</summary>
        QueuedIntent,
        /// <summary>
        /// A request that arrived after the input end frame, inside the step's
        /// late grace window, was consumed.
        /// </summary>
        LateGraceIntent,
        /// <summary>A branch sent the activation to a step of its own choosing.</summary>
        Branch,
        /// <summary>The step timed out under a policy that advances anyway.</summary>
        TimeoutAdvanced,
        /// <summary>The step timed out under a policy that holds the last step.</summary>
        TimeoutHeld,
        /// <summary>The step timed out and the ability ended normally.</summary>
        TimeoutCompleted,
        /// <summary>The step timed out and the ability ended as a cancellation.</summary>
        TimeoutCancelled,
        /// <summary>The last step ran out and the ability ended normally.</summary>
        Completed,
        /// <summary>A cancel request ended the ability.</summary>
        Cancelled,
        /// <summary>A cancel request ended the running step.</summary>
        StepCancelled,
        /// <summary>
        /// The step could not start: its targeting prelude found nothing and the
        /// ability requires a target.
        /// </summary>
        TargetingPreludeFailed,
        /// <summary>A queued request outlived the step that could have used it.</summary>
        IntentExpired
    }
}
