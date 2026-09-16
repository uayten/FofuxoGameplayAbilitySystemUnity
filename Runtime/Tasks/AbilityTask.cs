using System;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// How a task ended. <see cref="Running"/> is the only non-terminal state,
    /// and a task that reached a terminal one never ticks again.
    /// </summary>
    public enum AbilityTaskState
    {
        Running,
        Succeeded,
        Failed,
        Cancelled
    }

    /// <summary>
    /// Which engine step drives a task. <see cref="Update"/> is the default and
    /// what the ability timeline runs on, so a task added to an ability shares
    /// the timeline clock. <see cref="FixedUpdate"/> exists for movement that
    /// has to land on the physics step: a <c>MovePosition</c> issued from
    /// <c>Update</c> is overwritten by the next one before physics ever reads
    /// it, so travel driven from there loses whatever the frame rate outran.
    /// </summary>
    public enum AbilityTaskTickPhase
    {
        Update,
        FixedUpdate
    }

    /// <summary>
    /// One piece of latent work belonging to one activation: a wait, a target
    /// acquisition, a displacement. A task is runtime state, never authoring
    /// data, and it is owned by the scope that started it — an
    /// <see cref="AbilityInstance"/> for activation work, the
    /// <see cref="AbilitySystem"/> itself for the few reactions that belong to
    /// the actor rather than to any activation. Ending the owner cancels every
    /// task it holds, which is why a task can never survive it.
    /// </summary>
    public abstract class AbilityTask
    {
        /// <summary>Where the task ended up, or Running while it has not.</summary>
        public AbilityTaskState State { get; private set; } = AbilityTaskState.Running;

        public bool IsRunning => State == AbilityTaskState.Running;
        public bool IsFinished => State != AbilityTaskState.Running;

        /// <summary>The system that owns the scope this task runs in.</summary>
        public AbilitySystem AbilitySystem { get; private set; }

        /// <summary>
        /// The activation that started the task, or null when the task belongs
        /// to the actor rather than to any one activation.
        /// </summary>
        public AbilityInstance Instance { get; private set; }

        /// <summary>The actor the task acts on.</summary>
        public GameObject Owner { get; private set; }

        /// <summary>
        /// Fires exactly once, with the terminal state, whatever ended the task.
        /// Reading <see cref="State"/> inside the handler tells success from
        /// cancellation without one event per outcome.
        /// </summary>
        public event Action<AbilityTask> Finished;

        public virtual AbilityTaskTickPhase TickPhase => AbilityTaskTickPhase.Update;

        /// <summary>
        /// True for tasks that move the owner. Those are not ticked directly:
        /// the system arbitrates between them first, because only one may own
        /// the body in a tick unless a conflict policy says otherwise.
        /// </summary>
        internal virtual bool IsMovement => false;

        internal void Attach(
            AbilitySystem abilitySystem, AbilityInstance instance, GameObject owner)
        {
            AbilitySystem = abilitySystem;
            Instance = instance;
            Owner = owner;
        }

        internal void Begin()
        {
            if (IsFinished)
            {
                return;
            }

            using (AbilityDiagnostics.TaskMarker.Auto())
            using (AbilityDiagnostics.Tasks.Begin())
            {
                OnStart();
            }
        }

        internal void Tick(float deltaTime)
        {
            if (IsFinished)
            {
                return;
            }

            using (AbilityDiagnostics.TaskMarker.Auto())
            using (AbilityDiagnostics.Tasks.Begin())
            {
                OnTick(Mathf.Max(0f, deltaTime));
            }
        }

        /// <summary>
        /// Ends the task from the outside. Idempotent, so a scope tearing down
        /// after the task already succeeded does not overwrite the outcome.
        /// </summary>
        internal void Cancel()
        {
            Finish(AbilityTaskState.Cancelled);
        }

        protected void Succeed()
        {
            Finish(AbilityTaskState.Succeeded);
        }

        protected void Fail()
        {
            Finish(AbilityTaskState.Failed);
        }

        protected virtual void OnStart()
        {
        }

        protected virtual void OnTick(float deltaTime)
        {
        }

        protected virtual void OnEnd(AbilityTaskState state)
        {
        }

        private void Finish(AbilityTaskState state)
        {
            if (IsFinished || state == AbilityTaskState.Running)
            {
                return;
            }

            State = state;
            OnEnd(state);
            if (AbilityDiagnostics.Enabled && AbilitySystem != null)
            {
                AbilitySystem.Record(AbilityEvent.TaskEnded(Instance?.Definition, this));
            }

            Finished?.Invoke(this);
        }
    }
}
