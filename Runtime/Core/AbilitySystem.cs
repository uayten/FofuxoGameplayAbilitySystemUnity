using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// The actor's ability runtime: activation rules, running activations,
    /// cooldowns, charges, tags, tasks, gameplay events and cues. Callers own
    /// intent - no player input and no AI decision lives here.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Fofuxo/Gameplay Ability System")]
    public sealed class AbilitySystem : MonoBehaviour
    {
        [SerializeField] private AbilityLoadout loadout;
        [SerializeField] private Animator animator;

        private readonly Dictionary<GameplayTag, int> grantedTagCounts = new();
        private readonly Dictionary<AbilityDefinition, GameplayEffectSpec> costCheckSpecs = new();
        private readonly Dictionary<GameplayTag, int> activeWindowTagCounts = new();
        private readonly HashSet<GameplayTag> looseTags = new();
        private readonly List<GameplayCueTrigger> firedCues = new();
        private readonly List<GameplayTag> openedWindowTags = new();
        private readonly List<GameplayTag> closedWindowTags = new();
        private readonly Dictionary<AbilityDefinition, float> charges = new();
        private readonly Dictionary<AbilityDefinition, float> chargeRestoreTimers = new();
        /// <summary>Reused acquisition buffer for the targeting prelude.</summary>
        private readonly AbilityTargetData assistTargets = new();

        private AbilityAnimationPlayer animationPlayer;
        /// <summary>
        /// Every running activation, oldest first. Exclusion groups decide how
        /// many there can be: with the default policy an activation refuses to
        /// start while anything is running, so this holds at most one and every
        /// singular accessor below reports exactly what it always did.
        /// </summary>
        private readonly List<AbilityInstance> activeInstances = new();
        /// <summary>Snapshot ticked over, so an effect may end an activation mid-loop.</summary>
        private readonly List<AbilityInstance> tickBuffer = new();
        /// <summary>Scratch list for the instances an activation has to displace.</summary>
        private readonly List<AbilityInstance> supersededBuffer = new();
        /// <summary>Scratch list for the instances a gameplay event may advance.</summary>
        private readonly List<AbilityInstance> eventAdvanceBuffer = new();
        /// <summary>
        /// Tasks belonging to the actor rather than to any activation. A hit
        /// reaction arriving while nothing is running has no activation to be
        /// owned by, and inventing one to host it would be worse than saying so.
        /// They end with the component, never silently outlive it.
        /// </summary>
        private readonly AbilityTaskScope actorTasks = new();
        /// <summary>Ticked over a copy: a task may end another one as it finishes.</summary>
        private readonly List<AbilityTask> taskBuffer = new();
        /// <summary>
        /// Separate from the tick buffer on purpose: a task ticking may raise the
        /// very event that notifies tasks, and the two passes must not share the
        /// list they are walking.
        /// </summary>
        private readonly List<AbilityTask> notifyBuffer = new();
        private readonly List<AbilityMoveTask> movementBuffer = new();
        /// <summary>Highest priority any movement task contends with this tick.</summary>
        private int movementPriorityCeiling;
        /// <summary>True once a task has taken the body for the tick being applied.</summary>
        private bool movementOwnerTaken;
        private IAbilityMotor motor;
        private bool motorResolved;
        /// <summary>Cached once: the owner's body never changes shape mid-run.</summary>
        private Collider[] ownerColliders;
        /// <summary>Created on the first record and never before; see <see cref="History"/>.</summary>
        private AbilityEventHistory history;
        /// <summary>Created on first use; see <see cref="Cues"/>.</summary>
        private GameplayCueDispatcher cues;

        /// <summary>
        /// The activation the singular accessors speak for: the oldest one still
        /// running. A concurrent ability started later never takes the primary
        /// slot from the combo that was already going.
        /// </summary>
        private AbilityInstance activeInstance =>
            activeInstances.Count > 0 ? activeInstances[0] : null;
        /// <summary>
        /// The ability of the primary activation - the oldest one still
        /// running - or null.
        /// </summary>
        public AbilityDefinition ActiveAbility => activeInstance?.Definition;
        /// <summary>
        /// The step that activation is on, or null for an ability with no
        /// timeline.
        /// </summary>
        public AbilityStep ActiveStep => activeInstance?.Step;
        /// <summary>Zero-based index of that step.</summary>
        public int ActiveStepIndex => activeInstance?.StepIndex ?? 0;
        /// <summary>True while an input would carry the activation into its next step.</summary>
        public bool IsComboWindowOpen => activeInstance?.IsComboWindowOpen ?? false;
        /// <summary>One-based timeline frame the primary activation has reached.</summary>
        public int ActiveFrame => activeInstance?.CurrentFrame ?? 0;

        /// <summary>
        /// Every running activation, oldest first. One entry unless an ability
        /// opted into a group policy that lets abilities coexist.
        /// </summary>
        public IReadOnlyList<AbilityInstance> ActiveInstances => activeInstances;
        /// <summary>
        /// How many activations are running. More than one only where an
        /// exclusion group allowed it.
        /// </summary>
        public int ActiveAbilityCount => activeInstances.Count;

        /// <summary>
        /// The last request to advance a step on the primary activation and what
        /// became of it — queued, consumed, expired, or refused for arriving
        /// past the step's deadline. Survives the request, so a caller can read
        /// back why the press it made did nothing.
        /// </summary>
        public AbilityStepIntent QueuedStepIntent =>
            activeInstance?.Intent ?? AbilityStepIntent.None;

        /// <summary>True while a request is queued and still waiting to be consumed.</summary>
        public bool HasQueuedStepAdvance => activeInstance?.HasPendingIntent ?? false;

        /// <summary>
        /// Last frame of the running step that still accepts a request for the
        /// next step, late grace included. Zero when the step accepts one until
        /// it completes, and when nothing is running.
        /// </summary>
        public int ComboInputDeadlineFrame => activeInstance?.ComboInputDeadlineFrame ?? 0;

        /// <summary>Seconds left before the running step stops accepting a request.</summary>
        public float ComboInputTimeRemaining => activeInstance?.ComboInputTimeRemaining ?? 0f;

        /// <summary>Seconds left in the running step.</summary>
        public float StepTimeRemaining => activeInstance?.StepTimeRemaining ?? 0f;

        /// <summary>
        /// True while the primary activation has run out of step timeline under
        /// the Hold Last Step timeout policy and is waiting for something to
        /// advance, complete, or cancel it.
        /// </summary>
        public bool IsHoldingLastStep => activeInstance?.IsHoldingLastStep ?? false;

        /// <summary>
        /// Why the last activation changed step or ended. Kept after the ability
        /// itself is gone, so a listener that reacts to an ending can still ask
        /// what caused it.
        /// </summary>
        public AbilityStepTransition LastTransition { get; private set; }

        /// <summary>
        /// What happened to this actor lately, timestamped and oldest first:
        /// every activation that was refused and why, every start, step
        /// change, ending with its cancel tag, cue, gameplay event, effect
        /// delivered or received, and task. Filled only while
        /// <see cref="AbilityDiagnostics.Enabled"/> is on; reading this
        /// property creates the ring, so a tool that only wants to know whether
        /// anything was recorded asks <see cref="HasHistory"/> first.
        /// </summary>
        public AbilityEventHistory History =>
            history ??= new AbilityEventHistory(AbilityDiagnostics.HistoryCapacity);

        /// <summary>True once the history exists, which is once something was recorded.</summary>
        public bool HasHistory => history != null;

        /// <summary>
        /// Evaluated every tick for an ability whose advancement is On
        /// Condition, while the running step's combo window is open. Assigned
        /// from game code when the condition belongs to the actor rather than to
        /// the ability type; it takes precedence over
        /// <see cref="TimelineAbilityDefinition.CanAdvanceStep"/>.
        ///
        /// Must be side-effect free: it is asked whether the step may advance,
        /// not told to advance it.
        /// </summary>
        public Func<AbilityInstance, bool> StepAdvanceCondition { get; set; }

        public IReadOnlyCollection<GameplayTag> ActiveTags
        {
            get
            {
                var tags = new List<GameplayTag>(looseTags);
                foreach (KeyValuePair<GameplayTag, int> entry in grantedTagCounts)
                {
                    if (entry.Value > 0 && !tags.Contains(entry.Key))
                    {
                        tags.Add(entry.Key);
                    }
                }

                return tags;
            }
        }
        /// <summary>
        /// Startup, Active or Recovery of the primary activation; null when
        /// nothing runs.
        /// </summary>
        public AbilityPhase? ActivePhase => activeInstance?.CurrentPhase;
        /// <summary>The context the primary activation started with.</summary>
        public AbilityContext? ActiveContext => activeInstance?.Context;
        /// <summary>
        /// What the running activation's targeting acquired. Null when nothing
        /// is active; empty when the ability has no targeting at all.
        /// </summary>
        public AbilityTargetData ActiveTargetData => activeInstance?.TargetData;
        /// <summary>True while the primary activation is moving its owner.</summary>
        public bool HasActiveDisplacement => activeInstance?.HasActiveDisplacement ?? false;

        /// <summary>
        /// Tasks the actor owns outside any activation — a hit reaction that
        /// landed while nothing was running. Activation work belongs on
        /// <see cref="AbilityInstance.Tasks"/> instead, where the activation
        /// teardown ends it.
        /// </summary>
        public AbilityTaskScope ActorTasks => actorTasks;

        /// <summary>
        /// Where ability movement reaches the world. Resolved once from an
        /// <see cref="IAbilityMotor"/> on the owner, or, when it has none, from
        /// a plain Rigidbody so a project that never opted into the seam keeps
        /// the movement it always had. Assignable, for a consumer that decides
        /// its motor at runtime.
        ///
        /// **Null when the owner has neither.** It used to hand back a wrapper
        /// around a null Rigidbody, which accepted every displacement and moved
        /// nothing — an ability that looked authored, activated, played its
        /// animation and travelled zero metres. An activation that needs
        /// movement is now refused up front; see <see cref="HasMotor"/>.
        /// </summary>
        public IAbilityMotor Motor
        {
            get
            {
                if (!motorResolved)
                {
                    IAbilityMotor component = GetComponentInChildren<IAbilityMotor>(true);
                    Rigidbody body = GetComponent<Rigidbody>();
                    motor = component ?? (body != null
                        ? new RigidbodyDisplacementMotor(body)
                        : null);
                    motorResolved = true;
                }

                return motor;
            }
            set
            {
                motor = value;
                motorResolved = true;
            }
        }

        /// <summary>
        /// Whether ability movement has anywhere to go on this actor. False
        /// means no <see cref="IAbilityMotor"/> and no Rigidbody, so every
        /// displacement window, approach and move task would silently travel
        /// nothing.
        /// </summary>
        public bool HasMotor => Motor != null;

        internal float PlannedDisplacementDistanceForTests =>
            activeInstance?.RemainingDisplacementDistanceForTests ?? 0f;
        /// <summary>
        /// True while at least one window in the current step is holding the tag.
        /// Unlike <see cref="HasTag"/>, this ignores ability-wide grants and loose tags.
        /// </summary>
        public bool IsStepTagWindowOpen(GameplayTag tag)
        {
            for (int i = 0; i < activeInstances.Count; i++)
            {
                if (activeInstances[i].IsTagWindowOpen(tag))
                {
                    return true;
                }
            }

            return false;
        }
        /// <summary>
        /// True while the owner must not move under its own input. Either the
        /// running step has not reached its movement unlock frame, or a step tag
        /// window is holding <see cref="CommonGameplayTags.MovementLocked"/> —
        /// the window is the authored form, and it releases movement without
        /// ending the ability.
        /// </summary>
        public bool IsMovementLocked
        {
            get
            {
                if (HasTag(CommonGameplayTags.MovementLocked))
                {
                    return true;
                }

                // Any running activation may hold movement: a concurrent ability
                // that locks the owner still locks it while another one runs free.
                for (int i = 0; i < activeInstances.Count; i++)
                {
                    AbilityInstance instance = activeInstances[i];
                    if (instance.Definition.IsMovementLockedDuring(instance))
                    {
                        return true;
                    }
                }

                return false;
            }
        }
        /// <summary>True while any activation is running.</summary>
        public bool IsActive => activeInstances.Count > 0;
        /// <summary>
        /// The abilities this actor is granted, in the order events are
        /// offered to them.
        /// </summary>
        public AbilityLoadout Loadout => loadout;
        /// <summary>An activation began.</summary>
        public event Action<AbilityDefinition> AbilityStarted;
        /// <summary>A running activation entered a new phase.</summary>
        public event Action<AbilityDefinition, AbilityPhase> AbilityPhaseChanged;
        /// <summary>An activation finished on its own terms.</summary>
        public event Action<AbilityDefinition> AbilityCompleted;
        /// <summary>An activation was ended by something else; the tag says what.</summary>
        public event Action<AbilityDefinition, GameplayTag> AbilityCancelled;
        /// <summary>Fires when a combo carries on into its next step.</summary>
        public event Action<AbilityDefinition, int> AbilityStepAdvanced;
        /// <summary>
        /// Fires for every step change and every ending, carrying why it
        /// happened. A step advance raises both this and
        /// <see cref="AbilityStepAdvanced"/>; only this one explains the cause.
        /// </summary>
        public event Action<AbilityStepTransition> StepTransitioned;
        /// <summary>
        /// Fires whenever a request to advance a step changes state: accepted,
        /// consumed, expired, or refused. Every accepted request reaches exactly
        /// one terminal state, so a listener can account for each one.
        /// </summary>
        public event Action<AbilityDefinition, AbilityStepIntent> StepIntentChanged;
        /// <summary>
        /// Fires when an ability with effect triggers completes without
        /// registering any hit.
        /// </summary>
        public event Action<AbilityDefinition, AbilityContext> AbilityWhiffed;

        /// <summary>
        /// Fires when the first step window for a tag opens (true) or the last
        /// one closes (false). Overlapping windows for the same tag produce one
        /// effective transition. <see cref="HasTag"/> may remain true when the
        /// same tag also comes from an ability-wide grant or a loose tag.
        /// </summary>
        public event Action<GameplayTag, bool> StepTagWindowChanged;
        /// <summary>
        /// Optional destination for activations, cues and endings, for a
        /// netcode layer to hang on. Null by default; nothing in the package
        /// needs it.
        /// </summary>
        public IAbilityReplicationSink ReplicationSink { get; set; }

        /// <summary>
        /// Fires for every cue event this actor is the subject of - a step cue,
        /// an effect that landed on it, a manual one - after the dispatcher's
        /// filters. Cosmetic only: game code presents and must never change
        /// gameplay state in response. Presenters that pool register with
        /// <see cref="Cues"/> instead and get the same parameters.
        /// </summary>
        public event Action<GameplayCueParameters> GameplayCueTriggered;

        /// <summary>
        /// Where this actor's cues are raised, kept while persistent, and
        /// presented. Effects landing on this actor raise theirs here through
        /// the effect container; step cues and <see cref="TriggerGameplayCue"/>
        /// raise here directly.
        /// </summary>
        public GameplayCueDispatcher Cues
        {
            get
            {
                if (cues == null)
                {
                    cues = new GameplayCueDispatcher(gameObject);
                    cues.CueRaised += OnCueRaised;
                }

                return cues;
            }
        }

        private void Awake()
        {
            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>(true);
            }
        }

        private void Start()
        {
            GrantLoadoutEffects();
        }

        private void OnEnable()
        {
            animationPlayer ??= new AbilityAnimationPlayer(animator);
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        /// <summary>
        /// Drives the tasks that asked for the physics step. Nothing on the
        /// ability timeline runs here: the timeline keeps its own clock, and
        /// this exists so travel that has to be felt — a knockback — lands where
        /// <c>MovePosition</c> is actually read.
        /// </summary>
        private void FixedUpdate()
        {
            TickFixed(Time.fixedDeltaTime);
        }

        internal void Tick(float deltaTime)
        {
            animationPlayer?.Tick(deltaTime);
            TickChargeRestore(deltaTime);

            if (activeInstances.Count == 0 && actorTasks.Count == 0)
            {
                return;
            }

            PrepareMovementArbitration(AbilityTaskTickPhase.Update);

            // Ticking a copy: an effect fired below may cancel this activation or
            // start another, and the list must be free to change underneath.
            tickBuffer.Clear();
            tickBuffer.AddRange(activeInstances);
            for (int i = 0; i < tickBuffer.Count; i++)
            {
                AbilityInstance instance = tickBuffer[i];
                if (!activeInstances.Contains(instance))
                {
                    continue;
                }

                TickInstance(instance, deltaTime);
            }

            tickBuffer.Clear();
            TickScope(actorTasks, AbilityTaskTickPhase.Update, deltaTime);
            PruneTaskScopes();
        }

        /// <summary>
        /// One pass over the tasks that run on the physics step, across every
        /// activation and the actor scope, with the same movement arbitration
        /// the frame pass uses.
        /// </summary>
        internal void TickFixed(float deltaTime)
        {
            if (activeInstances.Count == 0 && actorTasks.Count == 0)
            {
                return;
            }

            PrepareMovementArbitration(AbilityTaskTickPhase.FixedUpdate);

            tickBuffer.Clear();
            tickBuffer.AddRange(activeInstances);
            for (int i = 0; i < tickBuffer.Count; i++)
            {
                AbilityInstance instance = tickBuffer[i];
                if (!activeInstances.Contains(instance))
                {
                    continue;
                }

                TickScope(instance.Tasks, AbilityTaskTickPhase.FixedUpdate, deltaTime);
            }

            tickBuffer.Clear();
            TickScope(actorTasks, AbilityTaskTickPhase.FixedUpdate, deltaTime);
            PruneTaskScopes();
        }

        private void TickInstance(AbilityInstance instance, float deltaTime)
        {
            if (instance.Definition.RequiresTarget && instance.Context.Target == null)
            {
                ForceCancelAbility(instance, CommonGameplayTags.CancelTargetLost);
                return;
            }

            if (!TickPeriodicCost(instance, deltaTime))
            {
                return;
            }

            // A held step has run out of timeline. It keeps waiting for an
            // advance, so it stops consuming time instead of re-running its
            // timeout every frame.
            if (instance.IsHoldingLastStep)
            {
                TryAdvanceOnCondition(instance);
                TryConsumeQueuedIntent(instance);
                return;
            }

            bool completed;
            AbilityPhase previousPhase;
            firedCues.Clear();
            try
            {
                completed = instance.Tick(this, deltaTime, out previousPhase, firedCues);
            }
            catch (Exception exception)
            {
                // The consumer broke, not the game design: the activation ends
                // under its own tag so a handler can tell a bug from a roll
                // cancelling a swing.
                Debug.LogException(exception, this);
                ForceCancelAbility(instance, CommonGameplayTags.CancelFailed);
                return;
            }

            if (!activeInstances.Contains(instance))
            {
                return;
            }

            instance.SyncTagWindows(openedWindowTags, closedWindowTags);
            ApplyTagWindowChanges();

            for (int i = 0; i < firedCues.Count; i++)
            {
                RaiseStepCue(instance, firedCues[i]);
            }

            if (previousPhase != instance.CurrentPhase)
            {
                AbilityPhaseChanged?.Invoke(instance.Definition, instance.CurrentPhase);
            }

            // The activation tasks advance with the activation, and its movement
            // is arbitrated here rather than after the loop: a step that finishes
            // below starts the next step travel, and that one belongs to the
            // next tick, not to this one.
            TickScope(instance.Tasks, AbilityTaskTickPhase.Update, deltaTime);

            if (!activeInstances.Contains(instance))
            {
                return;
            }

            if (TryAdvanceOnCondition(instance))
            {
                return;
            }

            if (TryConsumeQueuedIntent(instance))
            {
                return;
            }

            if (completed)
            {
                FinishStep(instance);
            }
        }

        private void OnDisable()
        {
            ClearRuntimeState();
        }

        private void OnDestroy()
        {
            ClearRuntimeState();
        }

        /// <summary>
        /// Runs every activation check and reports the outcome as a typed
        /// result. Side effect free, so it is safe to call from AI scoring or
        /// from UI that greys out an unusable ability.
        /// </summary>
        public AbilityActivationResult EvaluateActivation(
            AbilityDefinition ability, AbilityContext context)
        {
            return EvaluateActivationInternal(ability, context);
        }

        /// <summary>
        /// The message-only form, kept for callers that only ever log the
        /// reason. Prefer <see cref="EvaluateActivation"/> when the caller has
        /// to branch on why the activation failed.
        /// </summary>
        public bool CanActivate(
            AbilityDefinition ability,
            AbilityContext context,
            out string rejectionReason)
        {
            AbilityActivationResult result = EvaluateActivationInternal(ability, context);
            rejectionReason = result.Message;
            return result.IsAccepted;
        }
        /// <summary>
        /// Starts an ability with a context the caller built. False when the
        /// activation rules refused it - ask EvaluateActivation for the typed
        /// reason.
        /// </summary>
        public bool TryActivate(AbilityDefinition ability, AbilityContext context)
        {
            return TryActivate(ability, context, out _);
        }

        /// <summary>
        /// Activates the ability and reports the rejection code when it does
        /// not start. The result is <see cref="AbilityActivationResult.Accepted"/>
        /// only when the ability actually became active.
        /// </summary>
        public bool TryActivate(
            AbilityDefinition ability,
            AbilityContext context,
            out AbilityActivationResult result)
        {
            return TryActivate(ability, context, null, null, out result);
        }

        /// <summary>
        /// The one attempt path. Every public activation ends here, so the
        /// rejection record, the evaluation and the start cannot disagree.
        /// </summary>
        private bool TryActivate(
            AbilityDefinition ability,
            AbilityContext context,
            AbilityTargetData suppliedTargets,
            GameplayEffectSpec cause,
            out AbilityActivationResult result)
        {
            result = EvaluateActivationInternal(ability, context);
            if (!result.IsAccepted)
            {
                RecordRejection(ability, in result);
                return false;
            }

            return ActivateInternal(ability, context, suppliedTargets, cause);
        }

        /// <summary>
        /// Activates with targets the caller already resolved, skipping local
        /// acquisition entirely. A scripted sequence, an AI that picked its
        /// victim, or a consumer with its own targeting reticle owns the
        /// selection; the ability owns everything after it.
        /// </summary>
        /// <param name="suppliedTargets">
        /// Copied into the activation, so the caller stays free to reuse its
        /// buffer. Null falls back to the ability's own target assist.
        /// </param>
        public bool TryActivateWithTargets(
            AbilityDefinition ability,
            AbilityContext context,
            AbilityTargetData suppliedTargets,
            out AbilityActivationResult result)
        {
            return TryActivate(ability, context, suppliedTargets, null, out result);
        }

        /// <summary>
        /// Activates the first granted ability whose GameplayEvent trigger
        /// matches. A running instance of that same ability consumes repeated
        /// events without restarting, allowing successive impacts to update its
        /// runtime work while its granted tags remain stable.
        /// </summary>
        public bool TryHandleGameplayEvent(
            GameplayTag eventTag,
            AbilityContext context,
            out AbilityDefinition triggeredAbility)
        {
            return TryHandleGameplayEvent(eventTag, context, null, out triggeredAbility);
        }

        /// <summary>
        /// The same, with the effect application that raised the event attached.
        /// The activation it starts reads it through
        /// <see cref="AbilityInstance.TriggeringSpec"/>, which is how a hit
        /// reaction learns what hit it — source, level, contact, knockback —
        /// without a struct being handed to anyone.
        ///
        /// Every granted ability whose trigger matches is a candidate, in
        /// loadout order, and the first one that activates wins. One that is
        /// refused — a required tag it lacks, the owner's i-frames, a running
        /// ability that will not be interrupted — passes the event to the next,
        /// so a guard reaction that requires <c>State.Attacking</c> can sit
        /// above the plain flinch and take the hit only while the owner swings.
        /// </summary>
        public bool TryHandleGameplayEvent(
            GameplayTag eventTag,
            AbilityContext context,
            GameplayEffectSpec cause,
            out AbilityDefinition triggeredAbility)
        {
            triggeredAbility = null;
            if (eventTag.IsEmpty)
            {
                return false;
            }

            if (AbilityDiagnostics.Enabled)
            {
                Record(AbilityEvent.GameplayEventReceived(eventTag, context.Owner));
            }

            // Waiting tasks hear every event, before anything decides what the
            // event does to the timeline. A task waiting on a tag is not
            // competing with step advancement for it: one moves the activation
            // on, the other resolves work inside the step it is already in.
            NotifyGameplayEventTasks(eventTag, context);
            if (loadout == null)
            {
                return false;
            }

            // A running ability that advances on this event owns it: the author
            // named the tag on the ability itself, so the event belongs to the
            // activation in flight before it belongs to any activation trigger.
            if (TryAdvanceStepOnEvent(eventTag, out triggeredAbility))
            {
                return true;
            }

            AbilityContext ownerContext = context.Owner == gameObject
                ? context
                : new AbilityContext(
                    gameObject,
                    context.Target,
                    context.Direction,
                    context.AimPoint);

            IReadOnlyList<AbilityDefinition> abilities = loadout.Abilities;
            for (int i = 0; i < abilities.Count; i++)
            {
                AbilityDefinition ability = abilities[i];
                if (ability == null ||
                    !TryFindMatchingTrigger(ability, eventTag, out AbilityActivationTrigger trigger))
                {
                    continue;
                }

                AbilityInstance running = FindInstance(ability);
                if (running != null)
                {
                    if (!trigger.RestartsWhenActive)
                    {
                        // Consumed without restarting: the running activation
                        // carries on, which is what a body still in flight wants.
                        triggeredAbility = ability;
                        return true;
                    }

                    // Decided before anything ends, like every interruption: a
                    // restart the owner would refuse must not cost it the
                    // reaction it already has.
                    if (!EvaluateActivationInternal(ability, ownerContext, skipExclusion: true)
                            .IsAccepted)
                    {
                        continue;
                    }

                    ForceCancelAbility(running, CommonGameplayTags.CancelSuperseded);
                }

                if (activeInstance != null &&
                    !TryClearTheWayForTrigger(ability, trigger, ownerContext))
                {
                    continue;
                }

                if (!TryActivate(ability, ownerContext, null, cause, out _))
                {
                    continue;
                }

                triggeredAbility = ability;
                return true;
            }

            return false;
        }

        private static bool TryFindMatchingTrigger(
            AbilityDefinition ability,
            GameplayTag eventTag,
            out AbilityActivationTrigger match)
        {
            IReadOnlyList<AbilityActivationTrigger> triggers = ability.ActivationTriggers;
            for (int t = 0; t < triggers.Count; t++)
            {
                if (triggers[t].Matches(eventTag))
                {
                    match = triggers[t];
                    return true;
                }
            }

            match = default;
            return false;
        }

        /// <summary>
        /// Completes an externally-finished ability only when it is still the
        /// active instance. Physics reactions use this after landing or settling.
        /// </summary>
        /// <summary>
        /// Whether an interrupting activation trigger may take the running
        /// abilities' place, and cancels them when it may.
        ///
        /// **The decision comes before the cancellation, and that order is the
        /// whole point.** Cancelling first let an incoming event strip the very
        /// tags that would have refused it: an owner mid-roll lost its
        /// `State.Invulnerable` to the cancel and then failed the blocked-tag
        /// check that i-frames exist to win. It also took down abilities whose
        /// own cancel policy refuses to be interrupted, which the group
        /// exclusion path has always respected and this one did not.
        ///
        /// The exclusion rule is skipped in the evaluation because clearing the
        /// way is exactly what this method is for; everything else — blocked
        /// tags, required tags, cooldown, charges, costs, range, motor — is
        /// answered against the world as it stands, before anything is ended.
        /// </summary>
        private bool TryClearTheWayForTrigger(
            AbilityDefinition ability,
            AbilityActivationTrigger trigger,
            AbilityContext ownerContext)
        {
            if (!trigger.InterruptActiveAbility)
            {
                if (AbilityDiagnostics.Enabled)
                {
                    RecordRejection(ability, Reject(
                        AbilityActivationRejection.AnotherAbilityActive,
                        $"'{ability.AbilityId}' was triggered while '{ActiveAbility.AbilityId}' " +
                        "is running, and its trigger does not interrupt."));
                }

                return false;
            }

            AbilityActivationResult evaluation =
                EvaluateActivationInternal(ability, ownerContext, skipExclusion: true);
            if (!evaluation.IsAccepted)
            {
                RecordRejection(ability, in evaluation);
                return false;
            }

            for (int i = 0; i < activeInstances.Count; i++)
            {
                if (!activeInstances[i].Definition.CanBeCancelledBy(
                        CommonGameplayTags.CancelSuperseded))
                {
                    if (AbilityDiagnostics.Enabled)
                    {
                        RecordRejection(ability, Reject(
                            AbilityActivationRejection.BlockedByUncancellableAbility,
                            $"'{activeInstances[i].Definition.AbilityId}' refuses " +
                            $"{CommonGameplayTags.CancelSuperseded}, so the trigger " +
                            $"for '{ability.AbilityId}' cannot clear the way."));
                    }

                    return false;
                }
            }

            ForceCancelActiveAbility(CommonGameplayTags.CancelSuperseded);
            return true;
        }
        /// <summary>
        /// Completes the running activation of the ability named, whether or
        /// not it is the primary one, so an ability that runs alongside others
        /// can end itself. False when that ability is not running.
        /// </summary>
        public bool TryCompleteActiveAbility(AbilityDefinition expectedAbility)
        {
            AbilityInstance instance = FindInstance(expectedAbility);
            if (instance == null)
            {
                return false;
            }

            CompleteAbility(instance, AbilityStepTransitionReason.Completed);
            return true;
        }

        /// <summary>
        /// Interrupts every running ability whose cancel policy accepts a
        /// request carrying <paramref name="cancelTag"/>. With one ability
        /// running — the default everywhere — this is the single cancellation it
        /// always was.
        /// </summary>
        /// <returns>True when at least one ability was interrupted.</returns>
        public bool TryCancelActiveAbility(GameplayTag cancelTag)
        {
            bool cancelled = false;
            for (int i = activeInstances.Count - 1; i >= 0; i--)
            {
                if (i >= activeInstances.Count)
                {
                    continue;
                }

                AbilityInstance instance = activeInstances[i];
                if (!instance.Definition.CanBeCancelledBy(cancelTag))
                {
                    continue;
                }

                CancelAbilityInternal(
                    instance, cancelTag, AbilityStepTransitionReason.Cancelled);
                cancelled = true;
            }

            return cancelled;
        }

        /// <summary>
        /// Interrupts one named ability when its cancel policy accepts the
        /// request, for a caller that must not disturb the others.
        /// </summary>
        public bool TryCancelAbility(AbilityDefinition ability, GameplayTag cancelTag)
        {
            AbilityInstance instance = FindInstance(ability);
            if (instance == null || !instance.Definition.CanBeCancelledBy(cancelTag))
            {
                return false;
            }

            CancelAbilityInternal(instance, cancelTag, AbilityStepTransitionReason.Cancelled);
            return true;
        }

        /// <summary>
        /// Ends the running step of the primary activation without deciding what
        /// the ability does next: the step's own timeout policy does that, so a
        /// combo ends on the step it reached while a Hold Last Step sequence
        /// keeps waiting. This is the step-scoped half of cancellation — use
        /// <see cref="TryCancelActiveAbility"/> to end the whole activation.
        /// </summary>
        public bool TryCancelActiveStep(GameplayTag cancelTag)
        {
            AbilityInstance instance = activeInstance;
            if (instance == null || !instance.Definition.CanBeCancelledBy(cancelTag))
            {
                return false;
            }

            ExpirePendingIntent(instance);
            if (!activeInstances.Contains(instance))
            {
                return true;
            }

            RaiseTransition(
                instance,
                AbilityStepTransitionReason.StepCancelled,
                instance.StepIndex,
                cancelTag);
            FinishStep(instance);
            return true;
        }

        /// <summary>
        /// Interrupts every running ability whatever its cancel policy says.
        /// Death, disable, and destruction go through here.
        /// </summary>
        public void ForceCancelActiveAbility(GameplayTag cancelTag)
        {
            for (int i = activeInstances.Count - 1; i >= 0; i--)
            {
                if (i < activeInstances.Count)
                {
                    ForceCancelAbility(activeInstances[i], cancelTag);
                }
            }
        }

        private void ForceCancelAbility(AbilityInstance instance, GameplayTag cancelTag)
        {
            if (instance != null && activeInstances.Contains(instance))
            {
                CancelAbilityInternal(
                    instance, cancelTag, AbilityStepTransitionReason.Cancelled);
            }
        }

        /// <summary>
        /// Buffers one input for the next step of a Manual combo. The input is
        /// held until the running step reaches its Combo Continue Frame and is
        /// rejected once the step passes its Combo Input End Frame, so the
        /// window is authored in frames on the step and nowhere else.
        /// </summary>
        public bool TryQueueStepAdvance()
        {
            return TryQueueStepAdvance(activeInstance, AbilityStepIntentSource.Input);
        }

        /// <summary>
        /// Buffers a request for a named ability rather than the primary one,
        /// for a caller driving a concurrent activation.
        /// </summary>
        public bool TryQueueStepAdvance(AbilityDefinition ability)
        {
            return TryQueueStepAdvance(FindInstance(ability), AbilityStepIntentSource.Input);
        }

        /// <summary>
        /// Carries an activation whose advancement is On Event into its next
        /// step. The event only queues the request: the step's combo window
        /// still decides when the advance happens, so an event and an input
        /// reach the next step through the same authored frames.
        /// </summary>
        /// <returns>True when a running activation accepted the event.</returns>
        public bool TryAdvanceStepOnEvent(GameplayTag eventTag)
        {
            return TryAdvanceStepOnEvent(eventTag, out _);
        }

        /// <param name="advancedAbility">
        /// The first ability that accepted the event, so a caller routing every
        /// gameplay event can report which one consumed it.
        /// </param>
        public bool TryAdvanceStepOnEvent(
            GameplayTag eventTag, out AbilityDefinition advancedAbility)
        {
            advancedAbility = null;
            if (eventTag.IsEmpty)
            {
                return false;
            }

            bool accepted = false;
            // A copy: consuming a late request advances a step, and an ability
            // that ends there leaves the list one shorter mid-loop.
            eventAdvanceBuffer.Clear();
            eventAdvanceBuffer.AddRange(activeInstances);
            for (int i = 0; i < eventAdvanceBuffer.Count; i++)
            {
                AbilityInstance instance = eventAdvanceBuffer[i];
                TimelineAbilityDefinition ability = instance.Timeline;
                if (ability == null ||
                    ability.StepAdvancement != AbilityStepAdvancement.OnEvent ||
                    ability.StepAdvanceEventTag != eventTag ||
                    !activeInstances.Contains(instance))
                {
                    continue;
                }

                if (TryQueueStepAdvance(instance, AbilityStepIntentSource.GameplayEvent))
                {
                    accepted = true;
                    advancedAbility ??= ability;
                }
            }

            eventAdvanceBuffer.Clear();
            return accepted;
        }

        /// <summary>
        /// Records a request to advance, if the activation is in a mode that
        /// waits for one and the running step still accepts it. A request that
        /// arrives inside the step's late grace frames is accepted too and
        /// advances at once, because the window it belonged to has closed.
        /// </summary>
        private bool TryQueueStepAdvance(
            AbilityInstance instance, AbilityStepIntentSource source)
        {
            if (instance?.Timeline == null ||
                !instance.Timeline.WaitsToAdvance ||
                !instance.HasNextStep)
            {
                return false;
            }

            AbilityStep step = instance.Step;
            if (step != null && !step.AcceptsComboInput(instance.CurrentFrame))
            {
                instance.RejectIntent(source, true);
                StepIntentChanged?.Invoke(instance.Definition, instance.Intent);
                return false;
            }

            bool isLate = step != null && step.IsLateComboInput(instance.CurrentFrame);
            instance.QueueIntent(source, isLate);
            StepIntentChanged?.Invoke(instance.Definition, instance.Intent);

            // A late request has already missed its window, so waiting for the
            // window to open would drop it. Consume it now.
            if (isLate)
            {
                TryConsumeQueuedIntent(instance);
            }

            return true;
        }

        /// <summary>
        /// Carries a buffered request into the next step as soon as the running
        /// step opens its combo window, or immediately when the request came in
        /// late and the window is already behind it.
        /// </summary>
        private bool TryConsumeQueuedIntent(AbilityInstance instance)
        {
            if (instance == null || !instance.HasPendingIntent || !instance.HasNextStep)
            {
                return false;
            }

            bool late = instance.Intent.IsLate;
            if (!late && !instance.IsComboWindowOpen)
            {
                return false;
            }

            return AdvanceStep(
                instance,
                late
                    ? AbilityStepTransitionReason.LateGraceIntent
                    : AbilityStepTransitionReason.QueuedIntent,
                instance.Intent.Source == AbilityStepIntentSource.GameplayEvent
                    ? instance.Timeline.StepAdvanceEventTag
                    : default);
        }

        /// <summary>
        /// Polls the advance condition of an On Condition activation while its
        /// step's combo window is open. The condition is asked, never told: a
        /// consumer that mutates state inside it will see it evaluated more than
        /// once per step.
        /// </summary>
        private bool TryAdvanceOnCondition(AbilityInstance instance)
        {
            if (instance?.Timeline == null ||
                instance.Timeline.StepAdvancement != AbilityStepAdvancement.OnCondition ||
                !instance.HasNextStep)
            {
                return false;
            }

            // A held step has already passed its window, so it keeps polling:
            // holding exists precisely to wait for something that has not
            // happened yet.
            if (!instance.IsHoldingLastStep && !instance.IsComboWindowOpen)
            {
                return false;
            }

            return EvaluateAdvanceCondition(instance) &&
                   AdvanceStep(instance, AbilityStepTransitionReason.QueuedIntent, default);
        }

        /// <summary>
        /// A delegate assigned on the system wins over the ability's own hook:
        /// the actor knows more than the ability type does, and two sources of
        /// truth that both vote would make the outcome depend on their order.
        /// </summary>
        private bool EvaluateAdvanceCondition(AbilityInstance instance)
        {
            return StepAdvanceCondition != null
                ? StepAdvanceCondition(instance)
                : instance.Timeline.CanAdvanceStep(instance);
        }

        /// <summary>
        /// A step reached the end of its timeline. Automatic combos chain on
        /// their own; every waiting mode consults its timeout policy, whose
        /// default ends the ability on the step it reached — which is the whole
        /// cancellation path a player combo needs.
        /// </summary>
        private void FinishStep(AbilityInstance instance)
        {
            TimelineAbilityDefinition ability = instance.Timeline;
            if (ability == null || !instance.HasNextStep)
            {
                ExpirePendingIntent(instance);
                CompleteAbility(instance, AbilityStepTransitionReason.Completed);
                return;
            }

            if (ability.StepAdvancement == AbilityStepAdvancement.Automatic)
            {
                AdvanceStep(instance, AbilityStepTransitionReason.AutomaticChain, default);
                return;
            }

            // Last chance for a request that was queued before the window ever
            // opened: the step is over, so the window will not open at all.
            if (instance.HasPendingIntent)
            {
                AdvanceStep(
                    instance,
                    instance.Intent.IsLate
                        ? AbilityStepTransitionReason.LateGraceIntent
                        : AbilityStepTransitionReason.QueuedIntent,
                    default);
                return;
            }

            switch (ability.StepTimeoutPolicy)
            {
                case AbilityStepTimeoutPolicy.AdvanceStep:
                    AdvanceStep(instance, AbilityStepTransitionReason.TimeoutAdvanced, default);
                    return;
                case AbilityStepTimeoutPolicy.CancelAbility:
                    CancelAbilityInternal(
                        instance,
                        CommonGameplayTags.CancelStepTimeout,
                        AbilityStepTransitionReason.TimeoutCancelled);
                    return;
                case AbilityStepTimeoutPolicy.HoldLastStep:
                    instance.BeginHoldingLastStep();
                    RaiseTransition(
                        instance, AbilityStepTransitionReason.TimeoutHeld, instance.StepIndex);
                    return;
                default:
                    CompleteAbility(instance, AbilityStepTransitionReason.TimeoutCompleted);
                    return;
            }
        }

        /// <summary>
        /// Moves the activation on. The destination is the following step unless
        /// a branch on the outgoing step names another one, so a branch and a
        /// plain advance share one path and one set of side effects.
        /// </summary>
        private bool AdvanceStep(
            AbilityInstance instance,
            AbilityStepTransitionReason reason,
            GameplayTag reasonTag)
        {
            AbilityDefinition ability = instance.Definition;

            // Whatever carried the activation here, a request still waiting was
            // spent doing it. Resolving in one place is what keeps "consumed
            // exactly once" true for every advance path, timeouts included.
            if (instance.TryResolveIntent(AbilityStepIntentStatus.Consumed))
            {
                StepIntentChanged?.Invoke(ability, instance.Intent);
            }

            int fromStepIndex = instance.StepIndex;
            AbilityStep outgoingStep = instance.Step;
            int targetIndex = fromStepIndex + 1;
            if (outgoingStep != null)
            {
                int branched = outgoingStep.ResolveNextStepIndex(targetIndex, HasTag);
                if (branched != targetIndex)
                {
                    targetIndex = branched;
                    reason = AbilityStepTransitionReason.Branch;
                }
            }

            if (!instance.TryMoveToStep(targetIndex, closedWindowTags))
            {
                return false;
            }

            // A window belongs to the step that authored it, so the outgoing
            // step's tags come off before the next step's timeline starts.
            ApplyTagWindowChanges();

            AbilityStep step = instance.Step;
            float stepApproachDistance = 0f;
            if (step != null && step.TargetAssist != null)
            {
                AbilityTargetData stepTargets = ResolveTargetAssist(
                    step.TargetAssist,
                    instance.Context,
                    out AbilityContext stepContext,
                    out stepApproachDistance);
                if (stepTargets != null)
                {
                    instance.Retarget(stepContext, stepTargets);
                }
                else if (!HasLiveTarget(instance) && ability.RequiresTarget)
                {
                    // The prelude found nothing and the activation has no target
                    // left to fall back on, so the step cannot start. Ending here
                    // is what keeps the ability from running on with no step.
                    CancelAbilityInternal(
                        instance,
                        CommonGameplayTags.CancelTargetLost,
                        AbilityStepTransitionReason.TargetingPreludeFailed);
                    return false;
                }
            }

            BeginStepDisplacement(instance, step, instance.Context, stepApproachDistance);
            PlayStepAnimation(step);
            instance.Timeline?.OnStepStarted(instance, instance.StepIndex);
            RaiseTransition(instance, reason, fromStepIndex, reasonTag);
            AbilityStepAdvanced?.Invoke(ability, instance.StepIndex);
            AbilityPhaseChanged?.Invoke(ability, instance.CurrentPhase);
            return true;
        }

        /// <summary>
        /// True while the activation still holds a target that exists. A
        /// destroyed <c>GameObject</c> compares equal to null through Unity's
        /// lifetime check, which is exactly what has to be caught here.
        /// </summary>
        private static bool HasLiveTarget(AbilityInstance instance)
        {
            return instance.Context.Target != null;
        }

        /// <summary>
        /// Retires a request the activation can no longer use, so every accepted
        /// request ends either consumed or expired and never simply vanishes.
        /// </summary>
        private void ExpirePendingIntent(AbilityInstance instance)
        {
            if (!instance.TryResolveIntent(AbilityStepIntentStatus.Expired))
            {
                return;
            }

            StepIntentChanged?.Invoke(instance.Definition, instance.Intent);
            RaiseTransition(
                instance, AbilityStepTransitionReason.IntentExpired, instance.StepIndex);
        }

        private void RaiseTransition(
            AbilityInstance instance,
            AbilityStepTransitionReason reason,
            int fromStepIndex)
        {
            RaiseTransition(instance, reason, fromStepIndex, default);
        }

        private void RaiseTransition(
            AbilityInstance instance,
            AbilityStepTransitionReason reason,
            int fromStepIndex,
            GameplayTag tag)
        {
            LastTransition = new AbilityStepTransition(
                instance.Definition, reason, fromStepIndex, instance.StepIndex, tag);
            // Starts and endings are recorded by the paths that own them, with
            // the target and the cancel tag those paths know; what is left here
            // is the movement inside a live activation.
            if (AbilityDiagnostics.Enabled &&
                reason != AbilityStepTransitionReason.Activated &&
                activeInstances.Contains(instance))
            {
                Record(AbilityEvent.Transitioned(LastTransition));
            }

            StepTransitioned?.Invoke(LastTransition);
        }

        private AbilityInstance FindInstance(AbilityDefinition ability)
        {
            if (ability == null)
            {
                return null;
            }

            for (int i = 0; i < activeInstances.Count; i++)
            {
                if (activeInstances[i].Definition == ability)
                {
                    return activeInstances[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Starts a task on the primary activation, which is where activation
        /// work belongs: ending that activation ends the task with it.
        /// </summary>
        /// <returns>
        /// The task, or null when nothing is running. A caller that needs the
        /// work to happen regardless uses <see cref="RunActorTask{T}"/> and takes
        /// responsibility for it having no activation to end it.
        /// </returns>
        public T RunTask<T>(T task) where T : AbilityTask
        {
            AbilityInstance instance = activeInstance;
            if (instance == null || task == null)
            {
                return null;
            }

            WarnIfMovementHasNowhereToGo(task);
            return instance.Tasks.Run(task, this, instance, gameObject);
        }

        /// <summary>
        /// Starts a task on a named activation rather than the primary one,
        /// for a consumer running several abilities at once.
        /// </summary>
        public T RunTask<T>(AbilityDefinition ability, T task) where T : AbilityTask
        {
            AbilityInstance instance = FindInstance(ability);
            if (instance == null || task == null)
            {
                return null;
            }

            WarnIfMovementHasNowhereToGo(task);
            return instance.Tasks.Run(task, this, instance, gameObject);
        }

        /// <summary>
        /// Starts a task owned by the actor instead of by an activation. Reach
        /// for it only when there genuinely is no owning activation — a hit
        /// reaction landing while nothing is running — because an actor task is
        /// released by the component, not by an ability ending.
        /// </summary>
        public T RunActorTask<T>(T task) where T : AbilityTask
        {
            WarnIfMovementHasNowhereToGo(task);
            return actorTasks.Run(task, this, null, gameObject);
        }

        /// <summary>
        /// A movement task on an actor with no motor runs its whole timeline and
        /// travels nothing, which reads as a broken animation rather than as a
        /// missing component. An authored displacement window is refused at
        /// activation; a task started from code has no such gate, so it says so
        /// once, when it starts.
        /// </summary>
        private void WarnIfMovementHasNowhereToGo(AbilityTask task)
        {
            if (task is AbilityMoveTask && !HasMotor)
            {
                Debug.LogError(
                    $"'{name}' started {task.GetType().Name} with no IAbilityMotor " +
                    "and no Rigidbody, so it will travel nothing.",
                    this);
            }
        }

        /// <summary>
        /// Pushes the owner along a knockback vector, as a movement task.
        ///
        /// This is the target-owned half of knockback: the attacker effect says
        /// how hard and how long, and the target decides here whether it moves
        /// at all — immunity, armour, poise, blocking and death all get to
        /// refuse before this is ever called. It replaces the per-consumer
        /// FixedUpdate loops that each reimplemented the same push.
        ///
        /// The task is owned by the actor, not by whatever activation happens to
        /// be running: a knockback is not part of the combo it interrupted, and
        /// hanging it there would let that combo cancellation eat the push. A
        /// hit-reaction ability that should own its own knockback — the design
        /// the roadmap is heading for — runs the task itself with
        /// <see cref="RunTask{T}(T)"/> and gets the activation lifetime with it.
        /// </summary>
        /// <returns>The task, or null when the hit carries no movement.</returns>
        public ApplyKnockbackTask ApplyKnockback(
            Vector3 velocity,
            float durationSeconds,
            GameObject source = null,
            AbilityMovementDirectionPolicy directionPolicy =
                AbilityMovementDirectionPolicy.Snapshot)
        {
            if (velocity.sqrMagnitude <= Mathf.Epsilon || durationSeconds <= 0f)
            {
                CancelKnockback();
                return null;
            }

            // One knockback at a time: a second hit replaces the push instead of
            // adding to it, which is what the consumer loops did by overwriting
            // their velocity field.
            CancelKnockback();
            return actorTasks.Run(
                new ApplyKnockbackTask(velocity, durationSeconds, source, directionPolicy),
                this,
                null,
                gameObject);
        }

        /// <summary>
        /// Stops any knockback immediately. Physics control taking over, a death,
        /// or a teleport all need the push gone in the same frame rather than one
        /// tick later.
        /// </summary>
        public void CancelKnockback()
        {
            CancelKnockbackIn(actorTasks);
            for (int i = 0; i < activeInstances.Count; i++)
            {
                CancelKnockbackIn(activeInstances[i].Tasks);
            }
        }

        private static void CancelKnockbackIn(AbilityTaskScope scope)
        {
            IReadOnlyList<AbilityTask> scopeTasks = scope.Tasks;
            for (int i = scopeTasks.Count - 1; i >= 0; i--)
            {
                if (scopeTasks[i] is ApplyKnockbackTask knockback && knockback.IsRunning)
                {
                    knockback.Cancel();
                }
            }

            scope.PruneFinished();
        }

        /// <summary>
        /// True while a knockback task is pushing the owner. Consumers read it to
        /// keep their own locomotion out of the way.
        /// </summary>
        public bool IsKnockbackActive
        {
            get
            {
                if (HasRunningKnockback(actorTasks))
                {
                    return true;
                }

                for (int i = 0; i < activeInstances.Count; i++)
                {
                    if (HasRunningKnockback(activeInstances[i].Tasks))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        private static bool HasRunningKnockback(AbilityTaskScope scope)
        {
            IReadOnlyList<AbilityTask> scopeTasks = scope.Tasks;
            for (int i = 0; i < scopeTasks.Count; i++)
            {
                if (scopeTasks[i] is ApplyKnockbackTask knockback && knockback.IsRunning)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Reports an input edge to the waiting tasks. Called by
        /// <see cref="AbilityInputRouter"/> on both edges, so a task sees exactly
        /// what the activation input policies see, and the core keeps knowing
        /// nothing about the Input System.
        /// </summary>
        public void NotifyAbilityInput(AbilityInputEdge edge, AbilityDefinition source = null)
        {
            CollectTasksForNotification();
            for (int i = 0; i < notifyBuffer.Count; i++)
            {
                if (notifyBuffer[i] is WaitInputTask wait)
                {
                    wait.NotifyInput(edge, source);
                }
            }

            notifyBuffer.Clear();
        }

        /// <summary>
        /// Reports a Unity Animation Event to the waiting tasks. Forwarded by
        /// <see cref="AbilityAnimationEventBridge"/>.
        /// </summary>
        public void NotifyAnimationEvent(string eventName)
        {
            if (string.IsNullOrWhiteSpace(eventName))
            {
                return;
            }

            CollectTasksForNotification();
            for (int i = 0; i < notifyBuffer.Count; i++)
            {
                if (notifyBuffer[i] is WaitAnimationEventTask wait)
                {
                    wait.NotifyAnimationEvent(eventName);
                }
            }

            notifyBuffer.Clear();
        }

        private void NotifyGameplayEventTasks(GameplayTag eventTag, AbilityContext context)
        {
            CollectTasksForNotification();
            for (int i = 0; i < notifyBuffer.Count; i++)
            {
                if (notifyBuffer[i] is WaitGameplayEventTask wait)
                {
                    wait.NotifyGameplayEvent(eventTag, context);
                }
            }

            notifyBuffer.Clear();
        }

        /// <summary>
        /// Snapshots every running task across every scope. A notified task may
        /// succeed and start another, so the list it is read from cannot be the
        /// live one.
        /// </summary>
        private void CollectTasksForNotification()
        {
            notifyBuffer.Clear();
            for (int i = 0; i < activeInstances.Count; i++)
            {
                AbilityTaskScope scope = activeInstances[i].Tasks;
                notifyBuffer.AddRange(scope.Tasks);
            }

            notifyBuffer.AddRange(actorTasks.Tasks);
        }
        /// <summary>
        /// Whether the ability is on cooldown: the owner holds one of the tags
        /// its Cooldown Gameplay Effect grants, whatever applied it.
        /// </summary>
        public bool IsOnCooldown(AbilityDefinition ability)
        {
            if (ability == null)
            {
                return false;
            }

            IReadOnlyList<GameplayTag> cooldownTags = ability.CooldownTags;
            for (int i = 0; i < cooldownTags.Count; i++)
            {
                if (HasTag(cooldownTags[i]))
                {
                    return true;
                }
            }

            return false;
        }

        /// <returns>Seconds left on the cooldown, or zero when it is ready.</returns>
        public float GetCooldownRemaining(AbilityDefinition ability)
        {
            GetCooldownTimeRemainingAndDuration(ability, out float remaining, out _);
            return remaining;
        }

        /// <summary>
        /// The longest-running active effect that grants one of the ability's
        /// cooldown tags: how long is left on it and how long it lasts in
        /// total, the pair a cooldown display needs. Both zero when ready.
        /// Named after Unreal's <c>GetCooldownTimeRemainingAndDuration</c>.
        /// </summary>
        public void GetCooldownTimeRemainingAndDuration(
            AbilityDefinition ability, out float remaining, out float duration)
        {
            remaining = 0f;
            duration = 0f;
            GameplayEffectContainer container = GameplayEffectContainer.Find(gameObject);
            if (ability == null || container == null)
            {
                return;
            }

            IReadOnlyList<GameplayTag> cooldownTags = ability.CooldownTags;
            IReadOnlyList<ActiveGameplayEffect> effects = container.ActiveEffects;
            for (int i = 0; i < effects.Count; i++)
            {
                ActiveGameplayEffect effect = effects[i];
                if (effect.IsInfinite || effect.RemainingDuration <= remaining ||
                    !GrantsAny(effect, cooldownTags))
                {
                    continue;
                }

                remaining = effect.RemainingDuration;
                duration = effect.TotalDuration;
            }
        }

        private static bool GrantsAny(ActiveGameplayEffect effect, IReadOnlyList<GameplayTag> tags)
        {
            IReadOnlyList<GameplayTag> granted = effect.Definition != null
                ? effect.Definition.GrantedTags
                : null;
            for (int t = 0; t < tags.Count; t++)
            {
                if (granted != null)
                {
                    for (int g = 0; g < granted.Count; g++)
                    {
                        if (granted[g] == tags[t])
                        {
                            return true;
                        }
                    }
                }

                IReadOnlyList<GameplayTag> dynamic = effect.Spec.DynamicGrantedTags;
                for (int g = 0; g < dynamic.Count; g++)
                {
                    if (dynamic[g] == tags[t])
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        // ------------------------------------------------------------- commit

        /// <summary>
        /// Whether the ability's Cost Gameplay Effect can be paid now: none of
        /// its additive modifiers would take an attribute below its minimum.
        /// Unreal's <c>CheckCost</c>. A question — the spec it evaluates is
        /// never applied. An owner immune to the cost effect pays nothing, so
        /// it can always pay.
        /// </summary>
        public bool CheckCost(AbilityDefinition ability)
        {
            return CheckCost(ability, AbilityContext.FromTarget(gameObject, null), out _);
        }

        private bool CheckCost(AbilityDefinition ability, AbilityContext context, out string reason)
        {
            reason = null;
            GameplayEffectDefinition cost = ability != null ? ability.CostGameplayEffect : null;
            if (cost == null)
            {
                return true;
            }

            GameplayEffectContainer container = GameplayEffectContainer.Find(gameObject);
            if (container != null && !container.CanApply(cost))
            {
                return true;
            }

            TryGetComponent(out AttributeSet attributeSet);
            GameplayEffectSpec spec = CostCheckSpec(ability, cost, context);
            spec.CaptureSnapshots();
            spec.CalculateMagnitudes();
            for (int i = 0; i < spec.ModifierCount; i++)
            {
                GameplayEffectModifier modifier = spec.GetModifier(i);
                if (modifier.IsEmpty || modifier.Operation != AttributeOperation.Add)
                {
                    continue;
                }

                float magnitude = spec.ModifierMagnitudes[i];
                if (magnitude >= 0f)
                {
                    continue;
                }

                float current = attributeSet != null ? attributeSet.GetCurrent(modifier.Attribute) : 0f;
                float minimum = attributeSet != null ? attributeSet.GetMinimum(modifier.Attribute) : 0f;
                if (current + magnitude < minimum)
                {
                    reason = $"Insufficient {modifier.Attribute} for the cost: needs {-magnitude:0.##}, has {current:0.##}.";
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Commits a running activation that uses the Manual commit policy:
        /// checks the cost and the cooldown again, then pays the cost, spends a
        /// charge and applies a commit-time cooldown. Unreal's
        /// <c>CommitAbility</c>. A refused commit ends the activation with
        /// <see cref="CommonGameplayTags.CancelCommitFailed"/>. True when the
        /// ability is committed, including when it already was.
        /// </summary>
        public bool TryCommitAbility(AbilityDefinition ability)
        {
            AbilityInstance instance = FindInstance(ability);
            if (instance == null)
            {
                return false;
            }

            if (instance.IsCommitted)
            {
                return true;
            }

            if (IsOnCooldown(ability) || !CheckCost(ability, instance.Context, out _))
            {
                ForceCancelAbility(instance, CommonGameplayTags.CancelCommitFailed);
                return false;
            }

            CommitInternal(instance);
            return true;
        }

        private void CommitInternal(AbilityInstance instance)
        {
            AbilityDefinition ability = instance.Definition;
            instance.IsCommitted = true;
            ApplyCost(instance);
            instance.CostPeriodRemaining = ability.CostPeriod;
            ConsumeCharge(ability);
            if (ability.CooldownStartPolicy == AbilityCooldownStartPolicy.OnCommit)
            {
                ApplyCooldown(ability, instance.Context);
            }
        }

        private void ApplyCost(AbilityInstance instance)
        {
            GameplayEffectDefinition cost = instance.Definition.CostGameplayEffect;
            GameplayEffectContainer container = cost != null ? GameplayEffectContainer.For(gameObject) : null;
            if (container != null)
            {
                container.Apply(MakeOutgoingSpec(instance.Definition, cost, instance.Context));
            }
        }

        private void ApplyCooldown(AbilityDefinition ability, AbilityContext context)
        {
            GameplayEffectDefinition cooldown = ability.CooldownGameplayEffect;
            GameplayEffectContainer container = cooldown != null ? GameplayEffectContainer.For(gameObject) : null;
            if (container != null)
            {
                container.Apply(MakeOutgoingSpec(ability, cooldown, context));
            }
        }

        /// <summary>
        /// Pays the next installment of a periodic cost when it comes due. The
        /// first one that cannot be paid ends the activation.
        /// </summary>
        /// <returns>False when the activation was ended.</returns>
        private bool TickPeriodicCost(AbilityInstance instance, float deltaTime)
        {
            AbilityDefinition ability = instance.Definition;
            float period = ability.CostPeriod;
            if (!instance.IsCommitted || period <= 0f)
            {
                return true;
            }

            instance.CostPeriodRemaining -= deltaTime;
            while (instance.CostPeriodRemaining <= 0f)
            {
                if (!CheckCost(ability, instance.Context, out _))
                {
                    ForceCancelAbility(instance, CommonGameplayTags.CancelInsufficientCost);
                    return false;
                }

                ApplyCost(instance);
                instance.CostPeriodRemaining += period;
            }

            return true;
        }

        /// <summary>
        /// The spec a cost check evaluates, one per ability and reused: an AI
        /// asks <c>EvaluateActivation</c> for every granted ability every frame,
        /// and that question must not allocate. It is never applied.
        /// </summary>
        private GameplayEffectSpec CostCheckSpec(
            AbilityDefinition ability, GameplayEffectDefinition cost, AbilityContext context)
        {
            if (!costCheckSpecs.TryGetValue(ability, out GameplayEffectSpec spec) ||
                spec.Definition != cost)
            {
                spec = MakeOutgoingSpec(ability, cost, context);
                costCheckSpecs[ability] = spec;
                return spec;
            }

            ability.ConfigureOutgoingSpec(spec);
            return spec;
        }

        /// <summary>
        /// A spec for one of the ability's own effects, applied by and to the
        /// owner, handed to <see cref="AbilityDefinition.ConfigureOutgoingSpec"/>
        /// so the ability can fill its Set By Caller magnitudes.
        /// </summary>
        private GameplayEffectSpec MakeOutgoingSpec(
            AbilityDefinition ability, GameplayEffectDefinition effect, AbilityContext context)
        {
            var effectContext = new GameplayEffectContext(this, in context, ability);
            var spec = new GameplayEffectSpec(effect, in effectContext, gameObject, default);
            ability.ConfigureOutgoingSpec(spec);
            return spec;
        }

        /// <summary>
        /// Applies the loadout's Granted Gameplay Effects that are not already
        /// on the owner. Runs when the system starts and after a save restore,
        /// so an effect the record did not carry comes back all the same and
        /// one it did is not applied twice.
        /// </summary>
        internal void GrantLoadoutEffects()
        {
            IReadOnlyList<GameplayEffectDefinition> effects =
                loadout != null ? loadout.GrantedEffects : null;
            if (effects == null || effects.Count == 0)
            {
                return;
            }

            GameplayEffectContainer container = GameplayEffectContainer.For(gameObject);
            AbilityContext context = AbilityContext.FromTarget(gameObject, gameObject);
            for (int i = 0; i < effects.Count; i++)
            {
                GameplayEffectDefinition effect = effects[i];
                if (effect == null || container.IsActive(effect))
                {
                    continue;
                }

                var effectContext = new GameplayEffectContext(this, in context);
                container.Apply(new GameplayEffectSpec(effect, in effectContext, gameObject, default));
            }
        }
        /// <summary>
        /// Whether the actor holds a tag from any source: loose, granted by an
        /// ability or an effect, or open in a step window.
        /// </summary>
        public bool HasTag(GameplayTag tag)
        {
            return !tag.IsEmpty &&
                   (looseTags.Contains(tag) ||
                    grantedTagCounts.TryGetValue(tag, out int count) && count > 0);
        }
        /// <summary>
        /// Sets or clears a tag the game owns. Tags granted by abilities and
        /// effects are counted separately and are untouched by this.
        /// </summary>
        public void SetLooseTag(GameplayTag tag, bool enabled)
        {
            if (tag.IsEmpty)
            {
                return;
            }

            if (enabled)
            {
                looseTags.Add(tag);
            }
            else
            {
                looseTags.Remove(tag);
            }
        }
        /// <summary>
        /// The granted ability with this id, or null. The id is the asset's
        /// abilityId, never its file name.
        /// </summary>
        public AbilityDefinition FindAbility(string abilityId)
        {
            return loadout != null ? loadout.FindAbility(abilityId) : null;
        }

        /// <summary>
        /// Fires a cosmetic cue outside the ability timeline, for example an
        /// AI tell or a successful parry. Empty tags are ignored.
        /// </summary>
        public void TriggerGameplayCue(GameplayTag cue, AbilityContext context)
        {
            if (cue.IsEmpty)
            {
                return;
            }

            Cues.Execute(GameplayCueParameters.ForContext(
                cue, in context, activeInstance?.Definition));
        }

        /// <summary>
        /// A step cue, with the outcome the step can vouch for: effects apply
        /// before cues on the same frame, so whether anything was hit is known.
        /// A step with no effect triggers has nothing to miss.
        /// </summary>
        private void RaiseStepCue(AbilityInstance instance, in GameplayCueTrigger trigger)
        {
            bool landed = instance.Step == null ||
                          instance.Step.EffectTriggers.Count == 0 ||
                          instance.HasRegisteredHitOnStep(instance.StepIndex);
            if (!landed && trigger.SuppressOnMiss)
            {
                return;
            }

            Cues.Execute(GameplayCueParameters.ForAbility(
                trigger.Cue,
                instance,
                gameObject,
                landed ? GameplayCueOutcome.Landed : GameplayCueOutcome.Missed));
        }

        /// <summary>
        /// Every cue event this actor raises passes here once: into the history,
        /// out through the event, and - for what a netcode layer would forward -
        /// to the replication sink.
        /// </summary>
        private void OnCueRaised(GameplayCueParameters parameters)
        {
            if (AbilityDiagnostics.Enabled)
            {
                Record(AbilityEvent.Cue(
                    parameters.Ability,
                    parameters.Cue,
                    DescribeCueEvent(parameters.Event, parameters.Outcome)));
            }

            GameplayCueTriggered?.Invoke(parameters);
            if (parameters.Event == GameplayCueEvent.Execute ||
                parameters.Event == GameplayCueEvent.Add)
            {
                ReplicationSink?.OnGameplayCue(parameters.Cue, parameters.Context);
            }
        }

        private static string DescribeCueEvent(GameplayCueEvent cueEvent, GameplayCueOutcome outcome)
        {
            switch (cueEvent)
            {
                case GameplayCueEvent.Add: return "add";
                case GameplayCueEvent.WhileActive: return "while active";
                case GameplayCueEvent.Remove: return "remove";
                default:
                    switch (outcome)
                    {
                        case GameplayCueOutcome.Missed: return "execute (missed)";
                        case GameplayCueOutcome.Blocked: return "execute (blocked)";
                        case GameplayCueOutcome.Immune: return "execute (immune)";
                        case GameplayCueOutcome.Parried: return "execute (parried)";
                        default: return "execute";
                    }
            }
        }

        /// <summary>
        /// Applies an effect outside the ability timeline: parry rewards,
        /// cleanses, anything a consumer applies directly. The application
        /// context is the spec, not an activation — the ephemeral
        /// <see cref="AbilityInstance"/> this used to fabricate owned a task
        /// scope nothing would ever cancel.
        /// </summary>
        /// <returns>How many targets accepted the effect.</returns>
        public int ApplyGameplayEffect(
            GameplayEffectDefinition effect,
            AbilityContext context,
            AbilityDefinition source = null,
            int level = 1)
        {
            if (effect == null)
            {
                return 0;
            }

            return effect.ApplyFrom(
                new GameplayEffectContext(this, in context, source, level));
        }

        /// <summary>
        /// Effect-granted tags, refcounted alongside the ability-granted ones so
        /// <see cref="HasTag"/> answers for both. Called by the actor
        /// <see cref="GameplayEffectContainer"/>, which owns their lifetime.
        /// </summary>
        internal void AddEffectTag(GameplayTag tag)
        {
            AddTagInstance(tag);
        }

        internal void RemoveEffectTag(GameplayTag tag)
        {
            RemoveTagInstance(tag);
        }

        // ----------------------------------------------------------- diagnostics

        /// <summary>
        /// Puts one event in this actor's history and tells the diagnostics
        /// listeners. Nothing happens while diagnostics are off — not even the
        /// ring is created — which is what keeps a release player free of it.
        /// Callers that would have to build a message first check
        /// <see cref="AbilityDiagnostics.Enabled"/> themselves.
        /// </summary>
        internal void Record(in AbilityEvent recorded)
        {
            if (!AbilityDiagnostics.Enabled)
            {
                return;
            }

            History.Record(in recorded);
            AbilityDiagnostics.Raise(this, in recorded);
        }

        /// <summary>
        /// The refusal that used to vanish. Only an *attempt* is recorded:
        /// <see cref="EvaluateActivation"/> and <see cref="CanActivate"/> are
        /// questions, asked by AI scoring every frame, and stay side-effect
        /// free — the history would otherwise be nothing but them.
        /// </summary>
        private void RecordRejection(AbilityDefinition ability, in AbilityActivationResult result)
        {
            if (AbilityDiagnostics.Enabled)
            {
                Record(AbilityEvent.Rejected(ability, in result));
            }
        }

        /// <summary>
        /// Resolves the targeting prelude of an activation: acquire candidates
        /// around the context direction, snap the owner, propagate the chosen
        /// target, and calculate startup approach. Selection is instant and
        /// tagless; the ability keeps owning the timeline, movement, cooldown
        /// and animation.
        ///
        /// The assist produces <see cref="AbilityTargetData"/>. Everything the
        /// activation then knows about its target — the context actor, the
        /// facing, the approach — is read off that one result instead of being
        /// written into the context field by field.
        /// </summary>
        /// <returns>
        /// The acquisition, or null when the assist selected nothing and the
        /// context has to stay as it was.
        /// </returns>
        private AbilityTargetData ResolveTargetAssist(
            TargetAssistDefinition assist,
            AbilityContext context,
            out AbilityContext resolvedContext,
            out float approachDistance)
        {
            resolvedContext = context;
            approachDistance = 0f;
            GameObject owner = context.Owner;
            if (assist == null || owner == null || !assist.HasQuery)
            {
                return null;
            }

            Vector3 facing = context.Direction.sqrMagnitude > Mathf.Epsilon
                ? context.Direction
                : owner.transform.forward;
            assist.BuildQuery().Resolve(
                owner,
                owner.transform.position,
                facing,
                assistTargets);

            if (!TrySelectAssistTarget(assist, context, out AbilityTargetHit selected))
            {
                return null;
            }

            owner.transform.rotation = Quaternion.LookRotation(selected.Direction, Vector3.up);
            if (assist.ApproachTarget)
            {
                // The hit distance runs from the owner's origin to the target's
                // surface, so the owner's own half has to come off it before the
                // authored gap means the space left between the two bodies.
                approachDistance = Mathf.Max(
                    0f,
                    selected.Distance -
                    ResolveOwnerExtent(owner, selected.Direction) -
                    assist.ResolveStoppingGap());
            }

            GameObject target = selected.Actor;
            resolvedContext = new AbilityContext(
                owner,
                target,
                selected.Direction,
                target.transform.position);
            return assistTargets;
        }

        /// <summary>
        /// Applies the assist's lock policy to the acquisition. The default
        /// re-acquires every time, which is what a combo that should follow
        /// whoever is in front of the owner needs; the other two keep the
        /// activation committed to the enemy it started on.
        /// </summary>
        private bool TrySelectAssistTarget(
            TargetAssistDefinition assist,
            AbilityContext context,
            out AbilityTargetHit selected)
        {
            selected = default;
            GameObject held = context.Target;
            if (held != null && assist.LockPolicy != AbilityTargetLockPolicy.Reacquire)
            {
                for (int i = 0; i < assistTargets.Count; i++)
                {
                    if (assistTargets[i].Actor == held)
                    {
                        selected = assistTargets[i];
                        return true;
                    }
                }

                if (assist.LockPolicy == AbilityTargetLockPolicy.Lock)
                {
                    // Out of the query but still alive: the lock follows it, so
                    // the owner keeps turning toward a target that stepped away.
                    return TryDescribeHeldTarget(context.Owner, held, out selected);
                }
            }

            if (!assistTargets.HasTargets)
            {
                return false;
            }

            selected = assistTargets.Primary;
            return selected.Direction.sqrMagnitude > Mathf.Epsilon;
        }

        /// <summary>
        /// Rebuilds a hit for a locked target the query no longer reaches, from
        /// its transform alone. Distance is measured to its collider surface
        /// when it has one, so the approach still stops at the authored gap.
        /// </summary>
        private static bool TryDescribeHeldTarget(
            GameObject owner,
            GameObject held,
            out AbilityTargetHit hit)
        {
            hit = default;
            if (owner == null || held == null)
            {
                return false;
            }

            Collider collider = held.GetComponentInChildren<Collider>();
            Vector3 origin = owner.transform.position;
            Vector3 point = collider != null
                ? collider.ClosestPoint(origin)
                : held.transform.position;
            Vector3 planar = Vector3.ProjectOnPlane(point - origin, Vector3.up);
            if (planar.sqrMagnitude <= Mathf.Epsilon)
            {
                planar = Vector3.ProjectOnPlane(
                    held.transform.position - origin, Vector3.up);
            }

            float distance = planar.magnitude;
            if (distance <= Mathf.Epsilon)
            {
                return false;
            }

            Vector3 direction = planar / distance;
            hit = new AbilityTargetHit(
                held,
                collider,
                point,
                -direction,
                direction,
                distance);
            return true;
        }

        /// <summary>
        /// How far the owner's own body reaches along the approach direction,
        /// measured from its colliders rather than assumed. Without it the
        /// authored gap would be measured from the owner's origin, and a wide
        /// character would stop with its body still half a metre short.
        /// </summary>
        private float ResolveOwnerExtent(GameObject owner, Vector3 direction)
        {
            ownerColliders ??= GetComponentsInChildren<Collider>(true);
            return AbilityBodyExtent.Resolve(
                owner.transform.position, ownerColliders, direction);
        }

        /// <summary>
        /// The activation checks, in one place, producing the typed rejection
        /// code and the message together so the two can never disagree. Side
        /// effect free: nothing here may mutate runtime state or the definition.
        /// </summary>
        /// <param name="skipExclusion">
        /// Leaves out the mutual-exclusion rule, for a caller that is about to
        /// clear the way itself and needs to know whether the activation would
        /// be legal once it has. Every other rule still runs, against the world
        /// as it stands right now.
        /// </param>
        private AbilityActivationResult EvaluateActivationInternal(
            AbilityDefinition ability,
            AbilityContext context,
            bool skipExclusion = false)
        {
            if (ability == null)
            {
                return Reject(AbilityActivationRejection.NullAbility, "Ability is null.");
            }

            if (!ability.TryValidate(out string validationError))
            {
                return Reject(AbilityActivationRejection.InvalidDefinition, validationError);
            }

            AbilityActivationResult exclusion =
                skipExclusion ? AbilityActivationResult.Accepted : EvaluateExclusion(ability);
            if (!exclusion.IsAccepted)
            {
                return exclusion;
            }

            if (loadout == null || !loadout.Contains(ability))
            {
                return Reject(
                    AbilityActivationRejection.NotGranted,
                    "Ability is not granted by the current loadout.");
            }

            // The asset cannot check this: whether a displacement window has
            // anywhere to travel is a fact about the actor, not about the
            // ability, so it is decided here and only here.
            if (ability.RequiresMotor && !HasMotor)
            {
                return Reject(
                    AbilityActivationRejection.MissingMotor,
                    "The ability moves the owner, and the owner has no IAbilityMotor " +
                    "and no Rigidbody for movement to reach the world through.");
            }

            if (IsOnCooldown(ability))
            {
                return Reject(AbilityActivationRejection.OnCooldown, "Ability is on cooldown.");
            }

            if (ability.HasLimitedCharges && GetCharges(ability) < 1f)
            {
                return Reject(
                    AbilityActivationRejection.NoChargesLeft, "Ability has no charges left.");
            }

            if (!CheckCost(ability, context, out string costReason))
            {
                return Reject(AbilityActivationRejection.InsufficientAttribute, costReason);
            }

            foreach (GameplayTag requiredTag in ability.RequiredTags)
            {
                if (!HasTag(requiredTag))
                {
                    return Reject(
                        AbilityActivationRejection.MissingRequiredTag,
                        $"Required tag is missing: {requiredTag}.");
                }
            }

            foreach (GameplayTag blockedTag in ability.BlockedTags)
            {
                if (HasTag(blockedTag))
                {
                    return Reject(
                        AbilityActivationRejection.BlockedByTag,
                        $"Activation is blocked by tag: {blockedTag}.");
                }
            }

            if (ability.RequiresTarget && context.Target == null)
            {
                return Reject(
                    AbilityActivationRejection.MissingTarget, "Ability requires a target.");
            }

            if (context.Owner != null && context.Target != null)
            {
                Vector3 targetDirection = Vector3.ProjectOnPlane(
                    context.Target.transform.position - context.Owner.transform.position,
                    Vector3.up);
                float distance = targetDirection.magnitude;
                if (distance < ability.MinimumRange || distance > ability.MaximumRange)
                {
                    return Reject(
                        AbilityActivationRejection.OutOfRange,
                        "Target is outside the configured range.");
                }

                if (targetDirection.sqrMagnitude > Mathf.Epsilon)
                {
                    float angle = Vector3.Angle(
                        context.Owner.transform.forward,
                        targetDirection.normalized);
                    if (angle > ability.MaximumFacingAngle)
                    {
                        return Reject(
                            AbilityActivationRejection.OutsideFacingAngle,
                            "Target is outside the configured facing angle.");
                    }
                }
            }

            if (!ability.CanActivateAbility(this, in context, out string conditionReason))
            {
                return Reject(
                    AbilityActivationRejection.ConditionNotMet,
                    string.IsNullOrEmpty(conditionReason)
                        ? "The ability's activation condition is not met."
                        : conditionReason);
            }

            return AbilityActivationResult.Accepted;
        }


        /// <summary>
        /// Applies the ability's Active Effects to the owner and keeps their
        /// handles on the activation, so ending it removes exactly these.
        /// </summary>
        private void ApplyActiveEffects(AbilityInstance instance)
        {
            IReadOnlyList<GameplayEffectDefinition> effects = instance.Definition.ActiveEffects;
            if (effects.Count == 0)
            {
                return;
            }

            GameplayEffectContainer container = GameplayEffectContainer.For(gameObject);
            if (container == null)
            {
                Debug.LogWarning(
                    $"'{instance.Definition.AbilityId}' has Active Effects but " +
                    $"'{name}' has no GameplayEffectContainer to hold them.",
                    this);
                return;
            }

            for (int i = 0; i < effects.Count; i++)
            {
                if (effects[i] == null)
                {
                    continue;
                }

                GameplayEffectSpec spec = MakeOutgoingSpec(instance.Definition, effects[i], instance.Context);
                GameplayEffectApplicationResult result = container.Apply(spec);
                if (result.Handle.IsValid)
                {
                    instance.AddActiveEffectHandle(result.Handle);
                }
            }
        }

        private void RemoveActiveEffects(AbilityInstance instance)
        {
            List<GameplayEffectHandle> handles = instance.TakeActiveEffectHandles();
            if (handles == null)
            {
                return;
            }

            GameplayEffectContainer container = GameplayEffectContainer.For(gameObject);
            if (container == null)
            {
                return;
            }

            for (int i = 0; i < handles.Count; i++)
            {
                container.TryRemove(handles[i]);
            }
        }

        private static AbilityActivationResult Reject(
            AbilityActivationRejection rejection, string message)
        {
            return AbilityActivationResult.Rejected(rejection, message);
        }

        /// <summary>
        /// Whether the abilities already running let this one start. Side effect
        /// free, like every other activation check: a policy that cancels is
        /// only asked here whether it *could*, and does the cancelling in
        /// <see cref="ActivateInternal"/>.
        /// </summary>
        private AbilityActivationResult EvaluateExclusion(AbilityDefinition ability)
        {
            if (activeInstances.Count == 0)
            {
                return AbilityActivationResult.Accepted;
            }

            // One activation per ability, whatever the policy: a second one would
            // pay the cost and grant the tags again while the first still holds
            // them, and the singular accessors could not tell them apart.
            if (FindInstance(ability) != null)
            {
                return Reject(
                    AbilityActivationRejection.AnotherAbilityActive,
                    "This ability is already active.");
            }

            switch (ability.GroupExclusionPolicy)
            {
                case AbilityGroupExclusionPolicy.BlockWhileSameGroupActive:
                    for (int i = 0; i < activeInstances.Count; i++)
                    {
                        AbilityDefinition running = activeInstances[i].Definition;
                        if (ability.SharesGroupWith(running))
                        {
                            return Reject(
                                AbilityActivationRejection.BlockedByGroup,
                                $"Group '{ability.GroupTag}' is held by " +
                                $"'{running.AbilityId}'.");
                        }
                    }

                    return AbilityActivationResult.Accepted;

                case AbilityGroupExclusionPolicy.CancelSameGroup:
                case AbilityGroupExclusionPolicy.CancelAnyActive:
                    bool sameGroupOnly =
                        ability.GroupExclusionPolicy ==
                        AbilityGroupExclusionPolicy.CancelSameGroup;
                    for (int i = 0; i < activeInstances.Count; i++)
                    {
                        AbilityDefinition running = activeInstances[i].Definition;
                        if (sameGroupOnly && !ability.SharesGroupWith(running))
                        {
                            continue;
                        }

                        if (!running.CanBeCancelledBy(CommonGameplayTags.CancelSuperseded))
                        {
                            return Reject(
                                AbilityActivationRejection.BlockedByUncancellableAbility,
                                $"'{running.AbilityId}' refuses to be cancelled.");
                        }
                    }

                    return AbilityActivationResult.Accepted;

                default:
                    return Reject(
                        AbilityActivationRejection.AnotherAbilityActive,
                        "Another ability is active.");
            }
        }

        /// <summary>
        /// Clears the way for an activation whose policy cancels instead of
        /// blocking. <see cref="EvaluateExclusion"/> has already established
        /// that every ability here accepts the request.
        /// </summary>
        private void CancelSupersededAbilities(AbilityDefinition ability)
        {
            AbilityGroupExclusionPolicy policy = ability.GroupExclusionPolicy;
            if (policy != AbilityGroupExclusionPolicy.CancelSameGroup &&
                policy != AbilityGroupExclusionPolicy.CancelAnyActive)
            {
                return;
            }

            bool sameGroupOnly = policy == AbilityGroupExclusionPolicy.CancelSameGroup;
            supersededBuffer.Clear();
            for (int i = 0; i < activeInstances.Count; i++)
            {
                AbilityInstance instance = activeInstances[i];
                if (sameGroupOnly && !ability.SharesGroupWith(instance.Definition))
                {
                    continue;
                }

                supersededBuffer.Add(instance);
            }

            for (int i = 0; i < supersededBuffer.Count; i++)
            {
                ForceCancelAbility(
                    supersededBuffer[i], CommonGameplayTags.CancelSuperseded);
            }

            supersededBuffer.Clear();
        }

        private bool ActivateInternal(
            AbilityDefinition ability,
            AbilityContext context,
            AbilityTargetData suppliedTargets,
            GameplayEffectSpec cause)
        {
            // Nothing before this point mutates state, so a policy that cancels
            // its way in only takes effect once the activation is certain.
            CancelSupersededAbilities(ability);

            AbilityContext resolvedContext = context;
            float assistApproachDistance = 0f;
            AbilityTargetData resolvedTargets = suppliedTargets;
            if (suppliedTargets == null)
            {
                AbilityStep firstStep = (ability as TimelineAbilityDefinition)?.FirstStep;
                TargetAssistDefinition activationAssist =
                    firstStep != null && firstStep.TargetAssist != null
                        ? firstStep.TargetAssist
                        : ability.TargetAssist;
                resolvedTargets = ResolveTargetAssist(
                    activationAssist,
                    context,
                    out resolvedContext,
                    out assistApproachDistance);
            }
            else if (context.Target == null && suppliedTargets.PrimaryActor != null)
            {
                resolvedContext = AbilityContext.FromTarget(
                    context.Owner, suppliedTargets.PrimaryActor);
            }

            var instance = new AbilityInstance(ability, resolvedContext, this, cause);
            activeInstances.Add(instance);
            instance.AdoptTargets(resolvedTargets);
            BeginStepDisplacement(
                instance,
                instance.Step,
                resolvedContext,
                assistApproachDistance);
            AddGrantedTags(ability);
            ApplyActiveEffects(instance);
            if (ability.CommitPolicy == AbilityCommitPolicy.OnActivation)
            {
                CommitInternal(instance);
            }

            PlayStepAnimation(instance.Step);
            ability.OnActivated(instance);
            // No timeline, no step to have started. An ability without one
            // hears OnActivated and nothing else until it ends.
            instance.Timeline?.OnStepStarted(instance, instance.StepIndex);

            RaiseTransition(instance, AbilityStepTransitionReason.Activated, instance.StepIndex);
            if (AbilityDiagnostics.Enabled)
            {
                Record(AbilityEvent.Started(ability, instance.StepIndex, resolvedContext.Target));
            }

            AbilityStarted?.Invoke(ability);
            AbilityPhaseChanged?.Invoke(ability, instance.CurrentPhase);
            ReplicationSink?.OnAbilityActivated(ability, resolvedContext);
            return true;
        }

        /// <summary>
        /// The one teardown both endings share. Anything an activation holds —
        /// tag windows, granted tags, a pending advance request, the animation,
        /// its slot in the running list — is released here, so a new ending can
        /// never forget half of it.
        /// </summary>
        private AbilityContext EndActivation(
            AbilityInstance instance,
            bool cancelled,
            GameplayTag cancelTag,
            AbilityStepTransitionReason reason)
        {
            AbilityDefinition ability = instance.Definition;
            AbilityContext endedContext = instance.Context;
            // Tasks come off first, before any hook runs. A movement task that
            // survived one line past this point would tick again on an ended
            // activation, which is precisely what a cancellation must not do.
            instance.CancelTasks();
            ExpirePendingIntent(instance);
            instance.CloseTagWindows(closedWindowTags);
            ApplyTagWindowChanges();
            RemoveGrantedTags(ability);
            RemoveActiveEffects(instance);

            // The hook runs while the activation is still the running one and
            // after its tags have come off, which is the order derived abilities
            // were written against.
            if (cancelled)
            {
                ability.OnCancelled(instance, cancelTag);
            }
            else
            {
                ability.OnCompleted(instance);
            }

            activeInstances.Remove(instance);
            RaiseTransition(instance, reason, instance.StepIndex, cancelTag);
            StopAbilityAnimation(ability);
            return endedContext;
        }

        private void CompleteAbility(
            AbilityInstance instance, AbilityStepTransitionReason reason)
        {
            AbilityDefinition completedAbility = instance.Definition;
            bool hitAnything = instance.RegisteredHitCount > 0;
            AbilityStep completedStep = instance.Step;
            bool tracksHits = completedStep != null && completedStep.EffectTriggers.Count > 0;
            AbilityContext completedContext =
                EndActivation(instance, false, default, reason);

            if (instance.IsCommitted &&
                completedAbility.CooldownStartPolicy == AbilityCooldownStartPolicy.OnCompletion)
            {
                ApplyCooldown(completedAbility, completedContext);
            }

            if (AbilityDiagnostics.Enabled)
            {
                Record(AbilityEvent.Completed(completedAbility, instance.StepIndex, reason));
            }

            AbilityCompleted?.Invoke(completedAbility);
            ReplicationSink?.OnAbilityEnded(completedAbility, completedContext, true);
            if (tracksHits && !hitAnything)
            {
                if (AbilityDiagnostics.Enabled)
                {
                    Record(AbilityEvent.Whiffed(completedAbility));
                }

                AbilityWhiffed?.Invoke(completedAbility, completedContext);
            }
        }

        private void CancelAbilityInternal(
            AbilityInstance instance,
            GameplayTag cancelTag,
            AbilityStepTransitionReason reason)
        {
            AbilityDefinition cancelledAbility = instance.Definition;
            AbilityContext cancelledContext =
                EndActivation(instance, true, cancelTag, reason);
            if (AbilityDiagnostics.Enabled)
            {
                Record(AbilityEvent.Cancelled(
                    cancelledAbility, instance.StepIndex, cancelTag, reason));
            }

            AbilityCancelled?.Invoke(cancelledAbility, cancelTag);
            ReplicationSink?.OnAbilityEnded(cancelledAbility, cancelledContext, false);
        }

        /// <summary>
        /// Seconds already counted towards each ability's next charge. Read by
        /// <see cref="AbilityPersistence"/>; the system owns the timers.
        /// </summary>
        internal IReadOnlyDictionary<AbilityDefinition, float> ChargeRestoreElapsed =>
            chargeRestoreTimers;

        /// <summary>
        /// The tags set from outside the ability system, as opposed to the ones
        /// an ability or an effect grants while it lives. Only these are worth
        /// saving: the rest come back with what granted them.
        /// </summary>
        internal IReadOnlyCollection<GameplayTag> LooseTags => looseTags;


        /// <summary>
        /// Puts a charge pool back. A full pool is stored as no entry at all,
        /// which is the same shape <see cref="TickChargeRestore"/> leaves
        /// behind once an ability has recharged.
        /// </summary>
        internal void RestoreCharges(
            AbilityDefinition ability, float remaining, float restoreElapsed)
        {
            if (ability == null || !ability.HasLimitedCharges)
            {
                return;
            }

            float clamped = Mathf.Clamp(remaining, 0f, ability.MaxCharges);
            if (clamped >= ability.MaxCharges)
            {
                charges.Remove(ability);
                chargeRestoreTimers.Remove(ability);
                return;
            }

            charges[ability] = clamped;
            chargeRestoreTimers[ability] = Mathf.Max(0f, restoreElapsed);
        }

        /// <summary>
        /// Clears exactly what a record carries here - charges and loose tags;
        /// cooldowns are effects and go with the container - so a load replaces
        /// the actor's state instead of layering onto it. Running activations
        /// are not touched; they are not saved either.
        /// </summary>
        internal void ClearSavedState()
        {
            charges.Clear();
            chargeRestoreTimers.Clear();
            foreach (GameplayTag tag in new List<GameplayTag>(looseTags))
            {
                SetLooseTag(tag, false);
            }
        }


        /// <summary>
        /// Charges left on an ability, or positive infinity for one without a
        /// charge limit. A read for tooling and UI; the system spends and
        /// restores them itself.
        /// </summary>
        public float GetCharges(AbilityDefinition ability)
        {
            if (!ability.HasLimitedCharges)
            {
                return float.PositiveInfinity;
            }

            if (!charges.TryGetValue(ability, out float remaining))
            {
                remaining = ability.MaxCharges;
                charges[ability] = remaining;
            }

            return remaining;
        }

        private void ConsumeCharge(AbilityDefinition ability)
        {
            if (!ability.HasLimitedCharges)
            {
                return;
            }

            charges[ability] = Mathf.Max(0f, GetCharges(ability) - 1f);
            chargeRestoreTimers[ability] = 0f;
        }

        private void TickChargeRestore(float deltaTime)
        {
            if (charges.Count == 0)
            {
                return;
            }

            var drained = new List<AbilityDefinition>();
            foreach (KeyValuePair<AbilityDefinition, float> entry in charges)
            {
                AbilityDefinition ability = entry.Key;
                if (ability == null || entry.Value >= ability.MaxCharges)
                {
                    continue;
                }

                if (ability.ChargeRestoreTime > 0f)
                {
                    chargeRestoreTimers.TryGetValue(ability, out float elapsed);
                    elapsed += Mathf.Max(0f, deltaTime);
                    if (elapsed < ability.ChargeRestoreTime)
                    {
                        chargeRestoreTimers[ability] = elapsed;
                        continue;
                    }

                    chargeRestoreTimers[ability] = 0f;
                    charges[ability] = Mathf.Min(ability.MaxCharges, entry.Value + 1f);
                }
                else if (!IsOnCooldown(ability))
                {
                    charges[ability] = ability.MaxCharges;
                }

                if (charges[ability] >= ability.MaxCharges)
                {
                    drained.Add(ability);
                }
            }

            foreach (AbilityDefinition ability in drained)
            {
                charges.Remove(ability);
                chargeRestoreTimers.Remove(ability);
            }
        }


        /// <summary>
        /// Applies pending window changes as one batch. Closes run before opens,
        /// and notifications report only effective per-tag transitions, so
        /// overlapping windows and adjacent windows do not emit false edges.
        /// </summary>
        private void ApplyTagWindowChanges()
        {
            if (closedWindowTags.Count == 0 && openedWindowTags.Count == 0)
            {
                return;
            }

            var previousCounts = new Dictionary<GameplayTag, int>();
            var changedTags = new List<GameplayTag>();
            CaptureWindowTagCounts(closedWindowTags, previousCounts, changedTags);
            CaptureWindowTagCounts(openedWindowTags, previousCounts, changedTags);

            for (int i = 0; i < closedWindowTags.Count; i++)
            {
                GameplayTag tag = closedWindowTags[i];
                RemoveTagInstance(tag);
                RemoveWindowTagInstance(tag);
            }

            for (int i = 0; i < openedWindowTags.Count; i++)
            {
                GameplayTag tag = openedWindowTags[i];
                AddTagInstance(tag);
                AddWindowTagInstance(tag);
            }

            closedWindowTags.Clear();
            openedWindowTags.Clear();

            for (int i = 0; i < changedTags.Count; i++)
            {
                GameplayTag tag = changedTags[i];
                bool wasOpen = previousCounts[tag] > 0;
                bool isOpen = activeWindowTagCounts.TryGetValue(tag, out int count) && count > 0;
                if (wasOpen != isOpen)
                {
                    StepTagWindowChanged?.Invoke(tag, isOpen);
                }
            }
        }

        private void CaptureWindowTagCounts(
            List<GameplayTag> tags,
            Dictionary<GameplayTag, int> previousCounts,
            List<GameplayTag> changedTags)
        {
            for (int i = 0; i < tags.Count; i++)
            {
                GameplayTag tag = tags[i];
                if (previousCounts.ContainsKey(tag))
                {
                    continue;
                }

                activeWindowTagCounts.TryGetValue(tag, out int count);
                previousCounts.Add(tag, count);
                changedTags.Add(tag);
            }
        }

        private void AddWindowTagInstance(GameplayTag tag)
        {
            activeWindowTagCounts.TryGetValue(tag, out int count);
            activeWindowTagCounts[tag] = count + 1;
        }

        private void RemoveWindowTagInstance(GameplayTag tag)
        {
            if (!activeWindowTagCounts.TryGetValue(tag, out int count))
            {
                return;
            }

            if (count <= 1)
            {
                activeWindowTagCounts.Remove(tag);
            }
            else
            {
                activeWindowTagCounts[tag] = count - 1;
            }
        }

        private void ClearRuntimeState()
        {
            ForceCancelActiveAbility(CommonGameplayTags.CancelOwnerTeardown);
            // Whatever survived the activations belonged to the actor, and the
            // actor is going away too.
            actorTasks.CancelAll();
            for (int i = 0; i < activeInstances.Count; i++)
            {
                activeInstances[i].CancelTasks();
            }

            activeInstances.Clear();
            tickBuffer.Clear();
            taskBuffer.Clear();
            notifyBuffer.Clear();
            movementBuffer.Clear();
            supersededBuffer.Clear();
            eventAdvanceBuffer.Clear();
            animationPlayer?.Dispose();
            animationPlayer = null;
            // Persistent cues end with the actor: a presenter that pooled a loop
            // for one of them gets its Remove.
            cues?.RemoveAll();
            looseTags.Clear();
            grantedTagCounts.Clear();
            activeWindowTagCounts.Clear();
            openedWindowTags.Clear();
            closedWindowTags.Clear();
        }

        private void AddTagInstance(GameplayTag tag)
        {
            if (tag.IsEmpty)
            {
                return;
            }

            grantedTagCounts.TryGetValue(tag, out int count);
            grantedTagCounts[tag] = count + 1;
        }

        private void RemoveTagInstance(GameplayTag tag)
        {
            if (!grantedTagCounts.TryGetValue(tag, out int count))
            {
                return;
            }

            if (count <= 1)
            {
                grantedTagCounts.Remove(tag);
                return;
            }

            grantedTagCounts[tag] = count - 1;
        }

        private void AddGrantedTags(AbilityDefinition ability)
        {
            foreach (GameplayTag tag in ability.GrantedTags)
            {
                AddTagInstance(tag);
            }
        }

        private void RemoveGrantedTags(AbilityDefinition ability)
        {
            foreach (GameplayTag tag in ability.GrantedTags)
            {
                RemoveTagInstance(tag);
            }
        }

        /// <summary>
        /// Starts the travel the running step owns, as a task on the activation.
        ///
        /// The two kinds a step can have are different tasks, not two branches of
        /// one: an assist approach is a <see cref="MoveTowardTargetTask"/>,
        /// which knows it is closing a gap to a body, and authored displacement
        /// is a <see cref="MoveByDistanceTask"/>, which only knows a direction
        /// and a distance. A step cannot carry both — validation refuses it — so
        /// the choice is exclusive.
        ///
        /// Both keep the authored numbers exactly: the same frame window, the
        /// same duration derived from it, and for the approach the distance the
        /// prelude already measured, so no asset means anything different than
        /// it did.
        /// </summary>
        private void BeginStepDisplacement(
            AbilityInstance instance,
            AbilityStep step,
            AbilityContext context,
            float assistApproachDistance)
        {
            if (instance == null || step == null)
            {
                return;
            }

            int startFrame;
            int endFrame;
            AbilityMoveTask task;
            if (assistApproachDistance > Mathf.Epsilon)
            {
                startFrame = 1;
                endFrame = Mathf.Max(2, step.StartupEndFrame);
                task = new MoveTowardTargetTask(
                    context.Target,
                    assistApproachDistance,
                    AbilityDisplacement.WindowDurationSeconds(
                        startFrame, endFrame, step.FrameRate))
                {
                    Priority = AbilityMoveTask.ApproachPriority,
                };
            }
            else if (step.HasDisplacement)
            {
                startFrame = step.DisplacementStartFrame;
                endFrame = step.DisplacementEndFrame;
                task = new MoveByDistanceTask(
                    AbilityDisplacement.ResolveDirection(step.DisplacementDirection, context),
                    step.DisplacementDistance,
                    AbilityDisplacement.WindowDurationSeconds(
                        startFrame, endFrame, step.FrameRate))
                {
                    Priority = AbilityMoveTask.DisplacementPriority,
                };
            }
            else
            {
                return;
            }

            instance.RunDisplacement(task, this, startFrame, endFrame);
        }

        /// <summary>
        /// Finds the highest priority any movement task is contending with this
        /// tick, before a single one has moved. Deciding it up front is what
        /// lets the winner be applied in the middle of the activation loop, in
        /// the same place the old single displacement was, while still knowing
        /// about tasks belonging to activations that have not been reached yet.
        /// </summary>
        private void PrepareMovementArbitration(AbilityTaskTickPhase phase)
        {
            movementOwnerTaken = false;
            movementPriorityCeiling = int.MinValue;
            for (int i = 0; i < activeInstances.Count; i++)
            {
                RaiseMovementCeiling(activeInstances[i].Tasks, phase);
            }

            RaiseMovementCeiling(actorTasks, phase);
        }

        private void RaiseMovementCeiling(AbilityTaskScope scope, AbilityTaskTickPhase phase)
        {
            IReadOnlyList<AbilityTask> scopeTasks = scope.Tasks;
            for (int i = 0; i < scopeTasks.Count; i++)
            {
                if (scopeTasks[i] is AbilityMoveTask move &&
                    move.IsRunning &&
                    move.TickPhase == phase &&
                    move.IsWindowOpen &&
                    move.Priority > movementPriorityCeiling)
                {
                    movementPriorityCeiling = move.Priority;
                }
            }
        }

        /// <summary>
        /// Runs one scope for one phase: the plain tasks tick themselves, the
        /// movement ones go through arbitration and reach the world only through
        /// the motor.
        /// </summary>
        private void TickScope(
            AbilityTaskScope scope, AbilityTaskTickPhase phase, float deltaTime)
        {
            if (scope.Count == 0)
            {
                return;
            }

            taskBuffer.Clear();
            movementBuffer.Clear();
            scope.Collect(phase, taskBuffer);
            for (int i = 0; i < taskBuffer.Count; i++)
            {
                AbilityTask task = taskBuffer[i];
                if (task is AbilityMoveTask move)
                {
                    movementBuffer.Add(move);
                    continue;
                }

                task.Tick(deltaTime);
            }

            taskBuffer.Clear();
            for (int i = 0; i < movementBuffer.Count; i++)
            {
                ApplyMovementTask(movementBuffer[i], deltaTime);
            }

            movementBuffer.Clear();
        }

        /// <summary>
        /// One movement task, one tick. Only the highest-priority task that has
        /// not yet moved owns the body; ties break by the order the tasks were
        /// collected, which is activation age, oldest first — so with everything
        /// at the default priority this is exactly the rule the single
        /// displacement always followed. A loser does whatever its conflict
        /// policy says, and under the default that means spending nothing, so it
        /// still travels its full distance once it wins.
        ///
        /// Travel reaches the world only here, through the motor, which is what
        /// keeps <c>Rigidbody</c> and friends out of the tasks entirely.
        /// </summary>
        private void ApplyMovementTask(AbilityMoveTask move, float deltaTime)
        {
            if (!move.IsRunning || !move.IsWindowOpen)
            {
                return;
            }

            bool ownsBody = !movementOwnerTaken && move.Priority >= movementPriorityCeiling;
            if (!ownsBody)
            {
                switch (move.ConflictPolicy)
                {
                    case AbilityMovementConflictPolicy.Blend:
                        break;
                    case AbilityMovementConflictPolicy.Abort:
                        move.AbortForConflict();
                        return;
                    default:
                        return;
                }
            }

            if (!move.TickMovement(deltaTime, out Vector3 delta))
            {
                return;
            }

            if (ownsBody)
            {
                movementOwnerTaken = true;
            }

            Motor?.Move(delta, move.Collision);
        }

        private void PruneTaskScopes()
        {
            for (int i = 0; i < activeInstances.Count; i++)
            {
                activeInstances[i].Tasks.PruneFinished();
            }

            actorTasks.PruneFinished();
        }

        /// <summary>
        /// The motor a project gets without opting into the seam: the owner
        /// Rigidbody, moved with <c>MovePosition</c> the way root motion does.
        /// Velocity is never touched — the owner own motor owns velocity, the
        /// ability only adds travel — and nothing is swept, because the world
        /// collider and the level already constrain where the body may rest.
        /// A null body simply refuses to move, which is what an actor with no
        /// Rigidbody has always done.
        /// </summary>
        private sealed class RigidbodyDisplacementMotor : IAbilityMotor
        {
            private readonly Rigidbody body;

            public RigidbodyDisplacementMotor(Rigidbody body)
            {
                this.body = body;
            }

            public Vector3 Position => body != null ? body.position : Vector3.zero;

            public Vector3 Move(Vector3 delta, AbilityMovementCollision collision)
            {
                if (body == null || delta.sqrMagnitude <= Mathf.Epsilon)
                {
                    return Vector3.zero;
                }

                body.MovePosition(body.position + delta);
                return delta;
            }
        }

        private void PlayStepAnimation(AbilityStep step)
        {
            if (step == null)
            {
                return;
            }

            animationPlayer ??= new AbilityAnimationPlayer(animator);
            animationPlayer.Play(step.AnimationClip, step.AnimationBlendDuration);
        }

        private void StopAbilityAnimation(AbilityDefinition ability)
        {
            AbilityStep step = (ability as TimelineAbilityDefinition)?.FirstStep;
            animationPlayer?.Stop(step == null ? 0f : step.AnimationBlendDuration);
        }
    }
}
