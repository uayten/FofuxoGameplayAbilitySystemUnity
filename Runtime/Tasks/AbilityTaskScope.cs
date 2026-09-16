using System.Collections.Generic;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// The tasks one owner holds. It starts them, hands the running ones to the
    /// scheduler, drops the finished ones, and — the point of the whole type —
    /// cancels everything left when the owner ends. An activation owns one of
    /// these, so no task of its can outlive it.
    /// </summary>
    public sealed class AbilityTaskScope
    {
        private readonly List<AbilityTask> tasks = new();
        /// <summary>Torn down over a copy: OnEnd may start or end other tasks.</summary>
        private readonly List<AbilityTask> teardownBuffer = new();

        public int Count => tasks.Count;
        public IReadOnlyList<AbilityTask> Tasks => tasks;

        /// <summary>
        /// Registers and starts a task. One that finishes inside <c>OnStart</c>
        /// — a zero-length wait, an acquisition that resolves at once — is never
        /// registered, so it costs nothing afterwards.
        /// </summary>
        public T Run<T>(
            T task,
            AbilitySystem abilitySystem,
            AbilityInstance instance,
            GameObject owner)
            where T : AbilityTask
        {
            if (task == null)
            {
                return null;
            }

            task.Attach(abilitySystem, instance, owner);
            if (AbilityDiagnostics.Enabled && abilitySystem != null)
            {
                abilitySystem.Record(AbilityEvent.TaskStarted(instance?.Definition, task));
            }

            task.Begin();
            if (task.IsRunning)
            {
                tasks.Add(task);
            }

            return task;
        }

        /// <summary>
        /// Appends the running tasks of one phase, oldest first, to the
        /// scheduler buffer. Finished ones are skipped rather than removed:
        /// pruning happens once per tick, after everything has run.
        /// </summary>
        internal void Collect(AbilityTaskTickPhase phase, List<AbilityTask> into)
        {
            for (int i = 0; i < tasks.Count; i++)
            {
                AbilityTask task = tasks[i];
                if (task.IsRunning && task.TickPhase == phase)
                {
                    into.Add(task);
                }
            }
        }

        internal void PruneFinished()
        {
            for (int i = tasks.Count - 1; i >= 0; i--)
            {
                if (tasks[i].IsFinished)
                {
                    tasks.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Ends every task the owner still holds. Called from the single
        /// activation teardown, so completion and cancellation release tasks
        /// through the same line.
        /// </summary>
        internal void CancelAll()
        {
            if (tasks.Count == 0)
            {
                return;
            }

            teardownBuffer.Clear();
            teardownBuffer.AddRange(tasks);
            tasks.Clear();
            for (int i = 0; i < teardownBuffer.Count; i++)
            {
                teardownBuffer[i].Cancel();
            }

            teardownBuffer.Clear();
        }
    }
}
