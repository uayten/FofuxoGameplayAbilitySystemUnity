using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// What one entry in an actor's <see cref="AbilityEventHistory"/> is about.
    /// Each kind says which fields of <see cref="AbilityEvent"/> it fills.
    /// </summary>
    public enum AbilityEventKind
    {
        /// <summary>
        /// An activation attempt was refused. <c>Rejection</c> carries the
        /// code, <c>Message</c> the detail, <c>Ability</c> what was asked for.
        /// This is the entry that used to leave no trace at all.
        /// </summary>
        ActivationRejected,
        /// <summary>An ability began. <c>Actor</c> is the target it started against.</summary>
        AbilityStarted,
        /// <summary>
        /// The activation moved to another step, or a step ended without the
        /// ability ending. <c>Reason</c> and <c>StepIndex</c> say where it went.
        /// </summary>
        StepTransitioned,
        /// <summary>An ability ended normally.</summary>
        AbilityCompleted,
        /// <summary>An ability was cancelled. <c>Tag</c> carries the cancel tag.</summary>
        AbilityCancelled,
        /// <summary>An ability with effect triggers completed without a hit.</summary>
        AbilityWhiffed,
        /// <summary>A cosmetic cue fired. <c>Tag</c> carries it.</summary>
        GameplayCue,
        /// <summary>
        /// A gameplay event reached the actor. <c>Tag</c> carries it and
        /// <c>Actor</c> whoever the context named as owner.
        /// </summary>
        GameplayEvent,
        /// <summary>
        /// One of this actor's effects reached another actor and was taken.
        /// <c>Actor</c> is that target, <c>Effect</c> the effect. The
        /// source-side half of a hit.
        /// </summary>
        EffectDelivered,
        /// <summary>
        /// One of this actor's effects reached another actor and that actor
        /// refused it — blocked by tags, immune, or the effect itself declined.
        /// </summary>
        EffectRefused,
        /// <summary>
        /// An effect landed on this actor. <c>Actor</c> is its source,
        /// <c>Message</c> the application outcome. The target-side half.
        /// </summary>
        EffectApplied,
        /// <summary>Application tags, immunity or the effect itself refused a spec on this actor.</summary>
        EffectBlocked,
        /// <summary>An active effect on this actor expired or was removed.</summary>
        EffectRemoved,
        /// <summary>A task began. <c>Message</c> names its type.</summary>
        TaskStarted,
        /// <summary>A task finished. <c>Message</c> names its type and how it ended.</summary>
        TaskEnded
    }

    /// <summary>
    /// One timestamped thing that happened to an actor. A struct with no owned
    /// allocation, so recording one into the ring costs a copy: the strings it
    /// carries already existed for their own reasons.
    /// </summary>
    public readonly struct AbilityEvent
    {
        private AbilityEvent(
            AbilityEventKind kind,
            AbilityDefinition ability,
            int stepIndex,
            AbilityActivationRejection rejection,
            AbilityStepTransitionReason reason,
            GameplayTag tag,
            GameplayEffectDefinition effect,
            GameObject actor,
            string message)
        {
            Kind = kind;
            Time = UnityEngine.Time.time;
            RealTime = UnityEngine.Time.realtimeSinceStartup;
            Frame = UnityEngine.Time.frameCount;
            Ability = ability;
            StepIndex = stepIndex;
            Rejection = rejection;
            Reason = reason;
            Tag = tag;
            Effect = effect;
            Actor = actor;
            Message = message ?? string.Empty;
        }

        public AbilityEventKind Kind { get; }

        /// <summary>Scaled game time when it happened, <c>Time.time</c>.</summary>
        public float Time { get; }

        /// <summary>Wall-clock seconds since startup, unaffected by slow motion.</summary>
        public float RealTime { get; }

        /// <summary>The frame it happened on, <c>Time.frameCount</c>.</summary>
        public int Frame { get; }

        /// <summary>The ability involved, or null for an event with none.</summary>
        public AbilityDefinition Ability { get; }

        /// <summary>The step the activation was on, or -1 when there was none.</summary>
        public int StepIndex { get; }

        /// <summary>Why an activation was refused; <c>None</c> for every other kind.</summary>
        public AbilityActivationRejection Rejection { get; }

        /// <summary>Why a step changed; <c>None</c> for every other kind.</summary>
        public AbilityStepTransitionReason Reason { get; }

        /// <summary>The cancel, cue or event tag, empty when the kind carries none.</summary>
        public GameplayTag Tag { get; }

        /// <summary>The effect delivered, applied, blocked or removed.</summary>
        public GameplayEffectDefinition Effect { get; }

        /// <summary>The other actor: a target, a source, or an event's sender.</summary>
        public GameObject Actor { get; }

        /// <summary>
        /// Human-readable detail. Never parsed: the typed fields are the
        /// contract, this is what the window prints.
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// An activation was refused, with the typed rejection that refused
        /// it.
        /// </summary>
        public static AbilityEvent Rejected(
            AbilityDefinition ability, in AbilityActivationResult result)
        {
            return new AbilityEvent(
                AbilityEventKind.ActivationRejected,
                ability,
                -1,
                result.Rejection,
                AbilityStepTransitionReason.None,
                default,
                null,
                null,
                result.Message);
        }

        /// <summary>An activation began, on a step and against a target.</summary>
        public static AbilityEvent Started(AbilityDefinition ability, int stepIndex, GameObject target)
        {
            return new AbilityEvent(
                AbilityEventKind.AbilityStarted,
                ability,
                stepIndex,
                AbilityActivationRejection.None,
                AbilityStepTransitionReason.Activated,
                default,
                null,
                target,
                null);
        }

        /// <summary>A step transition, with the reason it happened.</summary>
        public static AbilityEvent Transitioned(in AbilityStepTransition transition)
        {
            return new AbilityEvent(
                AbilityEventKind.StepTransitioned,
                transition.Ability,
                transition.ToStepIndex,
                AbilityActivationRejection.None,
                transition.Reason,
                transition.Tag,
                null,
                null,
                null);
        }

        /// <summary>An activation completed on its own terms.</summary>
        public static AbilityEvent Completed(
            AbilityDefinition ability, int stepIndex, AbilityStepTransitionReason reason)
        {
            return new AbilityEvent(
                AbilityEventKind.AbilityCompleted,
                ability,
                stepIndex,
                AbilityActivationRejection.None,
                reason,
                default,
                null,
                null,
                null);
        }

        /// <summary>An activation was cancelled, with the tag that named the reason.</summary>
        public static AbilityEvent Cancelled(
            AbilityDefinition ability,
            int stepIndex,
            GameplayTag cancelTag,
            AbilityStepTransitionReason reason)
        {
            return new AbilityEvent(
                AbilityEventKind.AbilityCancelled,
                ability,
                stepIndex,
                AbilityActivationRejection.None,
                reason,
                cancelTag,
                null,
                null,
                null);
        }

        /// <summary>An activation ended without hitting anything.</summary>
        public static AbilityEvent Whiffed(AbilityDefinition ability)
        {
            return new AbilityEvent(
                AbilityEventKind.AbilityWhiffed,
                ability,
                -1,
                AbilityActivationRejection.None,
                AbilityStepTransitionReason.None,
                default,
                null,
                null,
                null);
        }

        /// <summary>A cue was raised.</summary>
        public static AbilityEvent Cue(AbilityDefinition ability, GameplayTag cue)
        {
            return Cue(ability, cue, null);
        }

        /// <summary>A cue lifecycle event; the message names the event and the outcome.</summary>
        public static AbilityEvent Cue(AbilityDefinition ability, GameplayTag cue, string message)
        {
            return new AbilityEvent(
                AbilityEventKind.GameplayCue,
                ability,
                -1,
                AbilityActivationRejection.None,
                AbilityStepTransitionReason.None,
                cue,
                null,
                null,
                message);
        }

        /// <summary>A gameplay event arrived, and who sent it.</summary>
        public static AbilityEvent GameplayEventReceived(GameplayTag eventTag, GameObject sender)
        {
            return new AbilityEvent(
                AbilityEventKind.GameplayEvent,
                null,
                -1,
                AbilityActivationRejection.None,
                AbilityStepTransitionReason.None,
                eventTag,
                null,
                sender,
                null);
        }

        /// <summary>
        /// An effect was delivered by an ability, and whether it found a
        /// target.
        /// </summary>
        public static AbilityEvent Delivered(
            AbilityDefinition ability,
            GameplayEffectDefinition effect,
            GameObject target,
            bool accepted)
        {
            return new AbilityEvent(
                accepted ? AbilityEventKind.EffectDelivered : AbilityEventKind.EffectRefused,
                ability,
                -1,
                AbilityActivationRejection.None,
                AbilityStepTransitionReason.None,
                default,
                effect,
                target,
                null);
        }

        /// <summary>
        /// An effect was applied to this actor, with the outcome the container
        /// returned.
        /// </summary>
        public static AbilityEvent Applied(
            GameplayEffectDefinition effect,
            GameObject source,
            GameplayEffectApplicationOutcome outcome)
        {
            return new AbilityEvent(
                AbilityEventKind.EffectApplied,
                null,
                -1,
                AbilityActivationRejection.None,
                AbilityStepTransitionReason.None,
                default,
                effect,
                source,
                Describe(outcome));
        }

        /// <summary>
        /// An effect was refused on this actor, with the outcome that refused
        /// it.
        /// </summary>
        public static AbilityEvent Blocked(
            GameplayEffectDefinition effect,
            GameObject source,
            GameplayEffectApplicationOutcome outcome)
        {
            return new AbilityEvent(
                AbilityEventKind.EffectBlocked,
                null,
                -1,
                AbilityActivationRejection.None,
                AbilityStepTransitionReason.None,
                default,
                effect,
                source,
                Describe(outcome));
        }

        /// <summary>An active effect was removed from this actor.</summary>
        public static AbilityEvent Removed(GameplayEffectDefinition effect, GameObject source)
        {
            return new AbilityEvent(
                AbilityEventKind.EffectRemoved,
                null,
                -1,
                AbilityActivationRejection.None,
                AbilityStepTransitionReason.None,
                default,
                effect,
                source,
                null);
        }

        /// <summary>A task started inside an activation.</summary>
        public static AbilityEvent TaskStarted(AbilityDefinition ability, AbilityTask task)
        {
            return new AbilityEvent(
                AbilityEventKind.TaskStarted,
                ability,
                -1,
                AbilityActivationRejection.None,
                AbilityStepTransitionReason.None,
                default,
                null,
                null,
                task == null ? null : task.GetType().Name);
        }

        /// <summary>A task ended.</summary>
        public static AbilityEvent TaskEnded(AbilityDefinition ability, AbilityTask task)
        {
            return new AbilityEvent(
                AbilityEventKind.TaskEnded,
                ability,
                -1,
                AbilityActivationRejection.None,
                AbilityStepTransitionReason.None,
                default,
                null,
                null,
                task == null ? null : task.GetType().Name + " " + Describe(task.State));
        }

        /// <summary>
        /// The event without its timestamp: ability, step and the detail the
        /// kind carries. What a window prints next to a time of its own choosing.
        /// </summary>
        public string Describe()
        {
            string ability = Ability == null ? string.Empty : Ability.AbilityId + " ";
            string step = StepIndex >= 0 ? $"step {StepIndex + 1} " : string.Empty;
            string detail;
            switch (Kind)
            {
                case AbilityEventKind.ActivationRejected:
                    detail = $"{Rejection}: {Message}";
                    break;
                case AbilityEventKind.StepTransitioned:
                    detail = Tag.IsEmpty ? Reason.ToString() : $"{Reason} ({Tag})";
                    break;
                case AbilityEventKind.AbilityCancelled:
                case AbilityEventKind.GameplayCue:
                case AbilityEventKind.GameplayEvent:
                    detail = Tag.Value;
                    break;
                case AbilityEventKind.EffectDelivered:
                case AbilityEventKind.EffectRefused:
                case AbilityEventKind.EffectApplied:
                case AbilityEventKind.EffectBlocked:
                case AbilityEventKind.EffectRemoved:
                    detail = (Effect == null ? "<effect>" : Effect.name) +
                             (Actor == null ? string.Empty : " ↔ " + Actor.name) +
                             (Message.Length == 0 ? string.Empty : " " + Message);
                    break;
                default:
                    detail = Message;
                    break;
            }

            return $"{ability}{step}{detail}".TrimEnd();
        }

        public override string ToString()
        {
            return $"[{Frame}] {Time:0.000}s {Kind} {Describe()}".TrimEnd();
        }

        /// <summary>
        /// Constant strings, so a recorded outcome allocates nothing: an enum's
        /// own <c>ToString</c> would box and format on every hit.
        /// </summary>
        private static string Describe(GameplayEffectApplicationOutcome outcome)
        {
            switch (outcome)
            {
                case GameplayEffectApplicationOutcome.Executed: return "executed";
                case GameplayEffectApplicationOutcome.Applied: return "applied";
                case GameplayEffectApplicationOutcome.Stacked: return "stacked";
                case GameplayEffectApplicationOutcome.Overflowed: return "overflowed";
                case GameplayEffectApplicationOutcome.Blocked: return "blocked";
                case GameplayEffectApplicationOutcome.Rejected: return "rejected";
                default: return "none";
            }
        }

        private static string Describe(AbilityTaskState state)
        {
            switch (state)
            {
                case AbilityTaskState.Succeeded: return "succeeded";
                case AbilityTaskState.Failed: return "failed";
                case AbilityTaskState.Cancelled: return "cancelled";
                default: return "running";
            }
        }
    }
}
