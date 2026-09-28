using System.Collections.Generic;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// One live activation. It owns the position inside the ability: which step
    /// is running and how far into that step's timeline the actor is. A combo
    /// is a single instance walking its steps, so cost, cooldown and granted
    /// tags are paid and held once for the whole chain.
    /// </summary>
    public sealed class AbilityInstance
    {
        private readonly HashSet<(int Step, int TriggerIndex, Object Key)> registeredHits = new();
        private readonly AbilityTargetData acquiredTargets = new();
        private readonly AbilityTargetData frameTargets = new();
        private HitShape frameQueryShape;
        private int frameQueryStep = -1;
        private int frameQueryFrame = -1;
        private bool hasFrameQuery;
        private bool[] executedEffects;
        private bool[] executedCues;
        private bool[] openTagWindows;
        private readonly AbilityTaskScope tasks = new();
        private AbilityMoveTask displacementTask;
        /// <summary>
        /// Handles of the ability's Active Effects on the owner, created only
        /// when the ability has any.
        /// </summary>
        private List<GameplayEffectHandle> activeEffectHandles;

        /// <summary>
        /// True once the activation has paid its cost and, under the On Commit
        /// policy, started its cooldown. An ability with the Manual commit
        /// policy runs uncommitted until it calls
        /// <c>AbilitySystem.TryCommitAbility</c>.
        /// </summary>
        public bool IsCommitted { get; internal set; }

        /// <summary>Seconds until the next installment of a periodic cost.</summary>
        internal float CostPeriodRemaining { get; set; }

        internal void AddActiveEffectHandle(GameplayEffectHandle handle)
        {
            (activeEffectHandles ??= new List<GameplayEffectHandle>()).Add(handle);
        }

        /// <summary>Hands the Active Effect handles over for removal, once.</summary>
        internal List<GameplayEffectHandle> TakeActiveEffectHandles()
        {
            List<GameplayEffectHandle> handles = activeEffectHandles;
            activeEffectHandles = null;
            return handles;
        }

        public AbilityInstance(AbilityDefinition definition, AbilityContext context)
            : this(definition, context, null, null)
        {
        }

        internal AbilityInstance(
            AbilityDefinition definition,
            AbilityContext context,
            AbilitySystem system,
            GameplayEffectSpec triggeringSpec)
        {
            Definition = definition;
            // Resolved once, here, rather than cast at every step lookup: an
            // ability either runs a timeline or it does not, and that cannot
            // change while the activation is alive.
            Timeline = definition as TimelineAbilityDefinition;
            Context = context;
            System = system;
            TriggeringSpec = triggeringSpec;
            StepIndex = 0;
            ResetStepState();
        }
        /// <summary>The ability this activation is running.</summary>
        public AbilityDefinition Definition { get; }
        /// <summary>
        /// Who acted, on what and in which direction, as the activation
        /// started.
        /// </summary>
        public AbilityContext Context { get; private set; }

        /// <summary>
        /// The system running this activation, or null for an instance built
        /// outside one — a test probe, a detached preview.
        /// </summary>
        public AbilitySystem System { get; }

        /// <summary>
        /// The effect application that started this activation, when a gameplay
        /// event raised by an effect did: a hit reaction reads what hit it here
        /// — source, level, contact, the knockback the effect authored — off the
        /// very spec that landed. Null for every other start.
        /// </summary>
        public GameplayEffectSpec TriggeringSpec { get; }
        /// <summary>Zero-based index of the step being run.</summary>
        public int StepIndex { get; private set; }
        /// <summary>
        /// The timeline this activation runs, or null for an ability that has
        /// none. Null is the normal shape for an ability whose work is code.
        /// </summary>
        public TimelineAbilityDefinition Timeline { get; }
        /// <summary>The step itself, or null for an ability with no timeline.</summary>
        public AbilityStep Step => Timeline == null ? null : Timeline.StepAt(StepIndex);
        /// <summary>Seconds since the current step started.</summary>
        public float ElapsedTime { get; private set; }
        /// <summary>One-based frame the current step has reached.</summary>
        public int CurrentFrame { get; private set; }
        /// <summary>Startup, Active or Recovery of this activation.</summary>
        public AbilityPhase CurrentPhase { get; private set; }
        /// <summary>How many hits this activation has registered across its steps.</summary>
        public int RegisteredHitCount => registeredHits.Count;

        /// <summary>
        /// What the activation's targeting acquired: the target assist result,
        /// or the targets a caller supplied instead. It is a snapshot taken when
        /// the step started, not a live view of the world.
        /// </summary>
        public AbilityTargetData TargetData => acquiredTargets;
        /// <summary>Whether another step follows the current one.</summary>
        public bool HasNextStep =>
            Timeline != null && StepIndex + 1 < Timeline.StepCount;

        /// <summary>True while an input would carry this activation into its next step.</summary>
        public bool IsComboWindowOpen =>
            HasNextStep && Step != null && Step.IsComboWindowOpen(CurrentFrame);

        /// <summary>
        /// The most recent request to advance, and what became of it. Kept after
        /// it resolves, so a caller can see whether the press it made advanced a
        /// step, expired, or arrived too late.
        /// </summary>
        public AbilityStepIntent Intent { get; private set; }

        /// <summary>True while a request is queued and still waiting to be consumed.</summary>
        public bool HasPendingIntent => Intent.IsPending;

        /// <summary>
        /// True once a waiting step ran out of timeline under the Hold Last Step
        /// timeout policy. The activation stays on its final frame until
        /// something advances, completes, or cancels it.
        /// </summary>
        public bool IsHoldingLastStep { get; private set; }

        /// <summary>
        /// Last frame of the running step that still accepts a request, late
        /// grace included. Zero means it accepts one until the step completes.
        /// </summary>
        public int ComboInputDeadlineFrame => Step?.ComboInputDeadlineFrame ?? 0;

        /// <summary>
        /// Seconds left before the running step stops accepting a request. When
        /// the step has no explicit deadline this is the time left in the step.
        /// </summary>
        public float ComboInputTimeRemaining
        {
            get
            {
                AbilityStep step = Step;
                if (step == null || !HasNextStep)
                {
                    return 0f;
                }

                int deadline = step.ComboInputDeadlineFrame;
                if (deadline <= 0)
                {
                    return StepTimeRemaining;
                }

                return Mathf.Max(0f, deadline / step.FrameRate - ElapsedTime);
            }
        }

        /// <summary>Seconds left in the running step, zero once it has run out.</summary>
        public float StepTimeRemaining
        {
            get
            {
                AbilityStep step = Step;
                return step == null ? 0f : Mathf.Max(0f, step.Duration - ElapsedTime);
            }
        }

        /// <summary>
        /// The tasks this activation owns. Ending the activation cancels every
        /// one of them, which is the whole contract: a task cannot outlive the
        /// activation that started it.
        /// </summary>
        public AbilityTaskScope Tasks => tasks;

        /// <summary>
        /// The travel the running step owns, or null when it has none. A step
        /// change replaces it, so an activation never carries the previous step
        /// displacement into the next one.
        /// </summary>
        public AbilityMoveTask DisplacementTask => displacementTask;
        /// <summary>
        /// True while this activation's displacement task is still moving the
        /// owner.
        /// </summary>
        public bool HasActiveDisplacement =>
            displacementTask != null && displacementTask.IsRunning;
        /// <summary>
        /// True while the authored displacement window of the current step is
        /// open.
        /// </summary>
        public bool IsDisplacementWindowOpen =>
            displacementTask != null && displacementTask.IsWindowOpen;

        /// <summary>
        /// The owner Rigidbody while a step travel is running. Informational
        /// only since movement started going through <see cref="IAbilityMotor"/>:
        /// which component actually moves the owner is the motor decision, and
        /// on a project that supplied one this may not be involved at all.
        /// </summary>
        public Rigidbody DisplacementBody =>
            displacementTask != null && Context.Owner != null
                ? Context.Owner.GetComponent<Rigidbody>()
                : null;

        internal float RemainingDisplacementDistanceForTests =>
            displacementTask?.RemainingDistance ?? 0f;

        /// <summary>
        /// Moves to the next step and restarts its timeline. Hit registration is
        /// keyed by step, so the next swing may hit a target the previous one
        /// already hit.
        /// </summary>
        /// <param name="closedTags">
        /// Receives every tag window the outgoing step still had open. A window
        /// belongs to its step, so carrying one into the next step would leak
        /// i-frames across a combo.
        /// </param>
        internal bool TryAdvanceStep(List<GameplayTag> closedTags = null)
        {
            if (!HasNextStep)
            {
                return false;
            }

            return TryMoveToStep(StepIndex + 1, closedTags);
        }

        /// <summary>
        /// Moves to an arbitrary step, which is what a branch needs: the target
        /// may sit anywhere in the list, including behind the current step, so a
        /// step can loop back onto itself.
        /// </summary>
        internal bool TryMoveToStep(int targetIndex, List<GameplayTag> closedTags = null)
        {
            if (Timeline == null ||
                targetIndex < 0 ||
                targetIndex >= Timeline.StepCount)
            {
                return false;
            }

            CloseTagWindows(closedTags);
            StepIndex = targetIndex;
            ResetStepState();
            return true;
        }

        /// <summary>
        /// Records an accepted request for the next step. Only one is held at a
        /// time: a second press inside the same window refreshes it rather than
        /// stacking, so a mashed button can never advance two steps.
        /// </summary>
        internal void QueueIntent(AbilityStepIntentSource source, bool isLate)
        {
            Intent = new AbilityStepIntent(
                source,
                AbilityStepIntentStatus.Queued,
                CurrentFrame,
                ComboInputDeadlineFrame,
                isLate);
        }

        /// <summary>
        /// Moves the held request to a terminal state. Anything but
        /// <see cref="AbilityStepIntentStatus.Queued"/> is already resolved and
        /// stays as it is, so a request is never counted twice.
        /// </summary>
        internal bool TryResolveIntent(AbilityStepIntentStatus status)
        {
            if (!Intent.IsPending)
            {
                return false;
            }

            Intent = Intent.WithStatus(status);
            return true;
        }

        /// <summary>
        /// Records a request refused on arrival, so the reason survives for a
        /// caller that wants to know why its press did nothing.
        /// </summary>
        internal void RejectIntent(AbilityStepIntentSource source, bool isLate)
        {
            Intent = new AbilityStepIntent(
                source,
                AbilityStepIntentStatus.Rejected,
                CurrentFrame,
                ComboInputDeadlineFrame,
                isLate);
        }

        internal void BeginHoldingLastStep()
        {
            IsHoldingLastStep = true;
        }

        /// <summary>
        /// Brings the open tag windows in line with <see cref="CurrentFrame"/>,
        /// reporting only the changes so the system applies each tag once.
        /// </summary>
        internal void SyncTagWindows(List<GameplayTag> openedTags, List<GameplayTag> closedTags)
        {
            AbilityStep step = Step;
            if (step == null || openTagWindows == null)
            {
                return;
            }

            IReadOnlyList<AbilityStepTagWindow> windows = step.TagWindows;
            int lastFrame = step.RecoveryEndFrame;
            for (int i = 0; i < openTagWindows.Length && i < windows.Count; i++)
            {
                bool shouldBeOpen = windows[i].Contains(CurrentFrame, lastFrame);
                if (shouldBeOpen == openTagWindows[i])
                {
                    continue;
                }

                openTagWindows[i] = shouldBeOpen;
                (shouldBeOpen ? openedTags : closedTags)?.Add(windows[i].Tag);
            }
        }

        /// <summary>
        /// Closes every open window. Called when the activation ends by any
        /// route, so a cancelled roll cannot leave its i-frames behind.
        /// </summary>
        internal void CloseTagWindows(List<GameplayTag> closedTags)
        {
            AbilityStep step = Step;
            if (step == null || openTagWindows == null)
            {
                return;
            }

            IReadOnlyList<AbilityStepTagWindow> windows = step.TagWindows;
            for (int i = 0; i < openTagWindows.Length && i < windows.Count; i++)
            {
                if (!openTagWindows[i])
                {
                    continue;
                }

                openTagWindows[i] = false;
                closedTags?.Add(windows[i].Tag);
            }
        }

        /// <summary>True while the window holding this tag is open.</summary>
        public bool IsTagWindowOpen(GameplayTag tag)
        {
            AbilityStep step = Step;
            if (step == null || openTagWindows == null)
            {
                return false;
            }

            IReadOnlyList<AbilityStepTagWindow> windows = step.TagWindows;
            for (int i = 0; i < openTagWindows.Length && i < windows.Count; i++)
            {
                if (openTagWindows[i] && windows[i].Tag == tag)
                {
                    return true;
                }
            }

            return false;
        }

        private void ResetStepState()
        {
            AbilityStep step = Step;
            // A request belongs to the step that accepted it. Carrying a pending
            // one into the next step would let a single press chain a whole combo.
            TryResolveIntent(AbilityStepIntentStatus.Consumed);
            IsHoldingLastStep = false;
            ElapsedTime = 0f;
            CurrentFrame = 0;
            CurrentPhase = step == null ? AbilityPhase.Startup : step.GetPhase(0);
            executedEffects = new bool[step == null ? 0 : step.EffectTriggers.Count];
            executedCues = new bool[step == null ? 0 : step.CueTriggers.Count];
            openTagWindows = new bool[step == null ? 0 : step.TagWindows.Count];
            // Travel belongs to the step that authored it. Cancelling here is
            // what stops a combo carrying the previous swing lunge into the next.
            displacementTask?.Cancel();
            displacementTask = null;

            hasFrameQuery = false;
            frameTargets.Clear();
        }

        /// <returns>True when the current step reached the end of its timeline.</returns>
        internal bool Tick(
            AbilitySystem abilitySystem,
            float deltaTime,
            out AbilityPhase previousPhase,
            List<GameplayCueTrigger> firedCues)
        {
            previousPhase = CurrentPhase;
            AbilityStep step = Step;
            if (step == null)
            {
                // How long it has been up is the only thing that can be said
                // about an activation with no timeline, so the clock still runs.
                ElapsedTime += Mathf.Max(0f, deltaTime);

                // No timeline means no last frame to complete on: the activation
                // stays until something ends it. A null step inside a definition
                // that *does* have a timeline is a broken asset instead, and
                // ending it is the safe reading of that.
                return Timeline != null;
            }

            ElapsedTime += Mathf.Max(0f, deltaTime);
            CurrentFrame = Mathf.Clamp(
                Mathf.FloorToInt(ElapsedTime * step.FrameRate) + 1,
                1,
                step.RecoveryEndFrame);
            CurrentPhase = step.GetPhase(CurrentFrame);

            for (int i = 0; i < step.EffectTriggers.Count; i++)
            {
                AbilityEffectTrigger trigger = step.EffectTriggers[i];
                if (executedEffects[i] || trigger.Frame > CurrentFrame)
                {
                    continue;
                }

                executedEffects[i] = true;
                trigger.Effect?.ApplyFrom(
                    new GameplayEffectContext(abilitySystem, this, i, trigger.Level));
            }

            for (int i = 0; i < step.CueTriggers.Count; i++)
            {
                GameplayCueTrigger cueTrigger = step.CueTriggers[i];
                if (executedCues[i] || cueTrigger.Frame > CurrentFrame)
                {
                    continue;
                }

                executedCues[i] = true;
                if (!cueTrigger.Cue.IsEmpty)
                {
                    firedCues?.Add(cueTrigger);
                }
            }

            return ElapsedTime >= step.Duration;
        }

        /// <summary>
        /// Snapshots the travel for the running step. Direction and body are
        /// resolved once by the system; the window consumes at constant speed
        /// (remaining distance over remaining duration), so hitches distribute
        /// instead of teleporting.
        /// </summary>
        /// <summary>
        /// Replaces the activation's target with the one a later step
        /// acquired. Effects read the context live, so a combo that starts
        /// on one enemy and turns toward another damages the one it faces.
        /// </summary>
        internal void Retarget(AbilityContext context)
        {
            Context = context;
        }

        /// <summary>
        /// Replaces both the context and the acquisition behind it, so the
        /// target data and the context can never name different actors.
        /// </summary>
        internal void Retarget(AbilityContext context, AbilityTargetData targets)
        {
            Context = context;
            AdoptTargets(targets);
        }

        /// <summary>
        /// Points the activation at what an acquisition found: the primary hit
        /// becomes the context target and its direction the facing, and the whole
        /// result becomes the activation target data. It is the same move the
        /// target-assist prelude makes, exposed for an
        /// <see cref="AcquireTargetsTask"/> that acquires mid-step.
        /// </summary>
        /// <returns>False when the acquisition found nothing to point at.</returns>
        public bool RetargetFromTargets(AbilityTargetData targets)
        {
            if (targets == null || !targets.HasTargets)
            {
                return false;
            }

            AbilityTargetHit primary = targets.Primary;
            if (primary.Actor == null)
            {
                return false;
            }

            Retarget(
                new AbilityContext(
                    Context.Owner,
                    primary.Actor,
                    primary.Direction,
                    primary.Actor.transform.position),
                targets);
            return true;
        }

        /// <summary>
        /// Takes over an acquisition produced elsewhere — the target assist, or
        /// a caller supplying its own targets. The data is copied, so the
        /// producer stays free to reuse its buffer.
        /// </summary>
        internal void AdoptTargets(AbilityTargetData targets)
        {
            acquiredTargets.Clear();
            if (targets == null)
            {
                return;
            }

            acquiredTargets.Begin(targets.Origin, targets.Direction);
            for (int i = 0; i < targets.Count; i++)
            {
                AbilityTargetHit hit = targets[i];
                acquiredTargets.Add(in hit);
            }
        }

        /// <summary>
        /// The shared target acquisition for the frame currently being ticked.
        /// Every effect that fires on the same trigger frame with the same shape
        /// gets the same result out of one physics query — which is the only
        /// reason a damage effect and a physics force on one frame cannot end up
        /// hitting two different enemies. Filtering stays per effect.
        /// </summary>
        public AbilityTargetData AcquireTargets(
            in HitShape shape,
            Vector3 aimPoint,
            Vector3 direction)
        {
            if (hasFrameQuery &&
                frameQueryStep == StepIndex &&
                frameQueryFrame == CurrentFrame &&
                frameQueryShape.Equals(shape))
            {
                return frameTargets;
            }

            AbilityTargetQuery.Acquire(
                in shape,
                Context.Owner,
                aimPoint,
                direction,
                frameTargets);
            frameQueryShape = shape;
            frameQueryStep = StepIndex;
            frameQueryFrame = CurrentFrame;
            hasFrameQuery = true;
            return frameTargets;
        }

        /// <summary>
        /// Starts unbounded step travel. The frame-window overload is what the
        /// system uses; this one exists for callers driving an instance directly.
        /// </summary>
        internal void BeginDisplacement(Vector3 direction, float distance, float duration)
        {
            RunDisplacement(
                new MoveByDistanceTask(direction, distance, duration), null, 0, 0);
        }

        internal void BeginDisplacement(
            Vector3 direction,
            float distance,
            float duration,
            int startFrame,
            int endFrame)
        {
            RunDisplacement(
                new MoveByDistanceTask(direction, distance, duration),
                null,
                startFrame,
                endFrame);
        }

        /// <summary>
        /// Makes a movement task the step travel: the previous one is cancelled,
        /// the frame window is applied when the caller has one, and the task
        /// joins this activation scope so ending the activation ends it too.
        /// </summary>
        internal AbilityMoveTask RunDisplacement(
            AbilityMoveTask task,
            AbilitySystem abilitySystem,
            int startFrame,
            int endFrame)
        {
            displacementTask?.Cancel();
            displacementTask = null;
            if (task == null)
            {
                return null;
            }

            if (startFrame > 0 && endFrame > 0)
            {
                task.SetFrameWindow(startFrame, endFrame);
            }

            tasks.Run(task, abilitySystem, this, Context.Owner);
            displacementTask = task.IsRunning ? task : null;
            return displacementTask;
        }

        /// <summary>
        /// Consumes one tick of the step travel. Returns false when there is
        /// nothing left to move this tick; the step never exceeds the remainder,
        /// so the total travelled distance equals the configured distance.
        /// </summary>
        internal bool TickDisplacement(float deltaTime, out Vector3 step)
        {
            step = Vector3.zero;
            return displacementTask != null &&
                displacementTask.TickMovement(deltaTime, out step);
        }

        /// <summary>
        /// Cancels every task this activation owns. Called by the single
        /// activation teardown, so completion and cancellation release tasks
        /// through one line and no task can outlive its activation.
        /// </summary>
        internal void CancelTasks()
        {
            displacementTask = null;
            tasks.CancelAll();
        }

        /// <summary>
        /// Starts a task in this activation's scope, so ending the activation
        /// ends the task. What a derived ability calls from its hooks; null when
        /// the instance runs under no system.
        /// </summary>
        public T RunTask<T>(T task) where T : AbilityTask
        {
            if (task == null || System == null)
            {
                return null;
            }

            return tasks.Run(task, System, this, Context.Owner);
        }

        /// <summary>
        /// Records that a trigger reached an actor, keyed by whatever the effect
        /// resolves the actor through — an attribute set, a physics body — so
        /// two colliders of one actor count once per trigger per step.
        /// </summary>
        public bool TryRegisterHit(int triggerIndex, Object key)
        {
            if (key == null)
            {
                return false;
            }

            return registeredHits.Add((StepIndex, triggerIndex, key));
        }

        /// <summary>
        /// Whether any effect trigger of one step reached an actor. What a step
        /// cue reads to tell a hit from a miss on the frame it fires.
        /// </summary>
        public bool HasRegisteredHitOnStep(int stepIndex)
        {
            foreach ((int Step, int TriggerIndex, Object Key) hit in registeredHits)
            {
                if (hit.Step == stepIndex)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
