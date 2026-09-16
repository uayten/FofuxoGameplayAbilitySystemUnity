using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// One persistent cue that is live on an actor: its handle, its latest
    /// parameters, and the active effect that owns it when one does. What a
    /// presenter registering late is caught up with.
    /// </summary>
    public sealed class ActiveGameplayCue
    {
        internal ActiveGameplayCue(
            GameplayCueHandle handle,
            in GameplayCueParameters parameters,
            GameplayEffectHandle effectHandle)
        {
            Handle = handle;
            Parameters = parameters;
            EffectHandle = effectHandle;
            AddedTime = Time.time;
        }

        public GameplayCueHandle Handle { get; }

        /// <summary>The parameters as of the last Add or WhileActive.</summary>
        public GameplayCueParameters Parameters { get; internal set; }

        public GameplayTag Cue => Parameters.Cue;

        /// <summary>The active effect this cue belongs to, or None for a cue added by code.</summary>
        public GameplayEffectHandle EffectHandle { get; }

        public float AddedTime { get; }

        public override string ToString()
        {
            return Parameters.ToString();
        }
    }

    /// <summary>
    /// Where an actor's cues are raised and presented. There is no central
    /// manager: each <see cref="AbilitySystem"/> owns one of these, presenters
    /// register with it, and every cue the actor is the subject of — its own
    /// step cues, the effects that land on it — goes through here. It keeps
    /// the persistent cues live on the actor, so removing an effect removes its
    /// cue and a presenter that arrives late is caught up with
    /// <see cref="GameplayCueEvent.WhileActive"/> and never a replayed burst.
    ///
    /// Filters run before presenters and may suppress or rewrite a cue.
    /// Identical bursts inside one frame — two effects on one hit both saying
    /// <c>Cue.Impact</c> — are batched into one when
    /// <see cref="BatchDuplicatesPerFrame"/> is on. A presenter that throws is
    /// logged and skipped; cosmetics never end an activation.
    /// </summary>
    public sealed class GameplayCueDispatcher
    {
        private readonly GameObject owner;
        private readonly List<IGameplayCuePresenter> presenters = new();
        private readonly List<IGameplayCueFilter> filters = new();
        private readonly List<ActiveGameplayCue> active = new();
        /// <summary>Presented over a copy: a presenter may register another.</summary>
        private readonly List<IGameplayCuePresenter> presenterBuffer = new();
        private readonly List<BatchKey> batchedThisFrame = new();
        private int batchFrame = -1;

        public GameplayCueDispatcher(GameObject owner)
        {
            this.owner = owner;
        }

        /// <summary>
        /// Drop a burst identical to one already raised this frame — same tag,
        /// same source, same ability. On by default; the count of what was
        /// dropped is on <see cref="BatchedCount"/>.
        /// </summary>
        public bool BatchDuplicatesPerFrame { get; set; } = true;

        /// <summary>Bursts dropped as duplicates since the dispatcher was created.</summary>
        public int BatchedCount { get; private set; }

        /// <summary>Every persistent cue live on the actor, oldest first.</summary>
        public IReadOnlyList<ActiveGameplayCue> ActiveCues => active;

        public int PresenterCount => presenters.Count;

        /// <summary>
        /// Fires for every cue event that reached the presenters, after the
        /// filters. The event form of <see cref="IGameplayCuePresenter"/>, for
        /// code that would rather subscribe than register.
        /// </summary>
        public event Action<GameplayCueParameters> CueRaised;

        /// <summary>
        /// Registers a presenter. One arriving while persistent cues are live
        /// is caught up with a <see cref="GameplayCueEvent.WhileActive"/> for
        /// each — never with the Add it missed, and never with a burst.
        /// </summary>
        public void AddPresenter(IGameplayCuePresenter presenter, bool catchUp = true)
        {
            if (presenter == null || presenters.Contains(presenter))
            {
                return;
            }

            presenters.Add(presenter);
            if (!catchUp)
            {
                return;
            }

            for (int i = 0; i < active.Count; i++)
            {
                GameplayCueParameters live = active[i].Parameters.WithEvent(
                    GameplayCueEvent.WhileActive, active[i].Handle);
                Present(presenter, in live);
            }
        }

        public bool RemovePresenter(IGameplayCuePresenter presenter)
        {
            return presenter != null && presenters.Remove(presenter);
        }

        public void AddFilter(IGameplayCueFilter filter)
        {
            if (filter != null && !filters.Contains(filter))
            {
                filters.Add(filter);
            }
        }

        public bool RemoveFilter(IGameplayCueFilter filter)
        {
            return filter != null && filters.Remove(filter);
        }

        /// <summary>
        /// Raises a burst cue once, now. Nothing is kept afterwards.
        /// </summary>
        /// <returns>False when a filter suppressed it or it was batched away.</returns>
        public bool Execute(in GameplayCueParameters parameters)
        {
            if (parameters.Cue.IsEmpty)
            {
                return false;
            }

            GameplayCueParameters raised = parameters.WithEvent(
                GameplayCueEvent.Execute, GameplayCueHandle.Next());
            if (!RunFilters(ref raised))
            {
                return false;
            }

            if (IsBatchedDuplicate(in raised))
            {
                BatchedCount++;
                return false;
            }

            Dispatch(in raised);
            return true;
        }

        /// <summary>
        /// Starts a persistent cue and keeps it until <see cref="Remove"/>.
        /// </summary>
        /// <param name="effectHandle">The active effect that owns it, so removing the effect removes the cue.</param>
        /// <returns>The handle to refresh or remove it by; None when a filter suppressed it.</returns>
        public GameplayCueHandle Add(
            in GameplayCueParameters parameters, GameplayEffectHandle effectHandle = default)
        {
            if (parameters.Cue.IsEmpty)
            {
                return GameplayCueHandle.None;
            }

            GameplayCueParameters raised = parameters.WithEvent(
                GameplayCueEvent.Add, GameplayCueHandle.Next());
            if (!RunFilters(ref raised))
            {
                return GameplayCueHandle.None;
            }

            active.Add(new ActiveGameplayCue(raised.Handle, in raised, effectHandle));
            Dispatch(in raised);
            return raised.Handle;
        }

        /// <summary>
        /// Updates a live persistent cue — a stack changed, a magnitude moved —
        /// and tells the presenters with <see cref="GameplayCueEvent.WhileActive"/>.
        /// The cue tag and the handle stay what the Add decided.
        /// </summary>
        public bool Refresh(GameplayCueHandle handle, in GameplayCueParameters updated)
        {
            ActiveGameplayCue cue = Find(handle);
            if (cue == null)
            {
                return false;
            }

            GameplayCueParameters raised = updated
                .WithCue(cue.Cue)
                .WithEvent(GameplayCueEvent.WhileActive, handle);
            cue.Parameters = raised;
            Dispatch(in raised);
            return true;
        }

        /// <summary>Ends a persistent cue. False when nothing by that handle is live.</summary>
        public bool Remove(GameplayCueHandle handle)
        {
            for (int i = 0; i < active.Count; i++)
            {
                if (active[i].Handle == handle)
                {
                    RemoveAt(i);
                    return true;
                }
            }

            return false;
        }

        /// <summary>Ends every persistent cue an active effect owns.</summary>
        public int RemoveForEffect(GameplayEffectHandle effectHandle)
        {
            if (!effectHandle.IsValid)
            {
                return 0;
            }

            int removed = 0;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                if (active[i].EffectHandle == effectHandle)
                {
                    RemoveAt(i);
                    removed++;
                }
            }

            return removed;
        }

        /// <summary>Ends every persistent cue, newest first. Teardown.</summary>
        public int RemoveAll()
        {
            int removed = active.Count;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                RemoveAt(i);
            }

            return removed;
        }

        public bool IsActive(GameplayCueHandle handle)
        {
            return Find(handle) != null;
        }

        public bool TryGetActive(GameplayCueHandle handle, out ActiveGameplayCue cue)
        {
            cue = Find(handle);
            return cue != null;
        }

        private ActiveGameplayCue Find(GameplayCueHandle handle)
        {
            if (!handle.IsValid)
            {
                return null;
            }

            for (int i = 0; i < active.Count; i++)
            {
                if (active[i].Handle == handle)
                {
                    return active[i];
                }
            }

            return null;
        }

        private void RemoveAt(int index)
        {
            ActiveGameplayCue cue = active[index];
            active.RemoveAt(index);
            GameplayCueParameters raised = cue.Parameters.WithEvent(
                GameplayCueEvent.Remove, cue.Handle);
            Dispatch(in raised);
        }

        private bool RunFilters(ref GameplayCueParameters parameters)
        {
            for (int i = 0; i < filters.Count; i++)
            {
                if (!filters[i].Filter(ref parameters) || parameters.Cue.IsEmpty)
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsBatchedDuplicate(in GameplayCueParameters parameters)
        {
            if (!BatchDuplicatesPerFrame)
            {
                return false;
            }

            int frame = Time.frameCount;
            if (frame != batchFrame)
            {
                batchFrame = frame;
                batchedThisFrame.Clear();
            }

            BatchKey key = new(parameters.Cue, parameters.Source, parameters.Ability);
            for (int i = 0; i < batchedThisFrame.Count; i++)
            {
                if (batchedThisFrame[i].Equals(key))
                {
                    return true;
                }
            }

            batchedThisFrame.Add(key);
            return false;
        }

        private void Dispatch(in GameplayCueParameters parameters)
        {
            CueRaised?.Invoke(parameters);
            if (presenters.Count == 0)
            {
                return;
            }

            presenterBuffer.Clear();
            presenterBuffer.AddRange(presenters);
            for (int i = 0; i < presenterBuffer.Count; i++)
            {
                Present(presenterBuffer[i], in parameters);
            }

            presenterBuffer.Clear();
        }

        /// <summary>
        /// Cosmetics never break gameplay: a presenter that throws is logged
        /// against the actor and the cue goes on to the next one.
        /// </summary>
        private void Present(IGameplayCuePresenter presenter, in GameplayCueParameters parameters)
        {
            try
            {
                presenter.OnGameplayCue(in parameters);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, owner);
            }
        }

        private readonly struct BatchKey : IEquatable<BatchKey>
        {
            private readonly GameplayTag cue;
            private readonly GameObject source;
            private readonly AbilityDefinition ability;

            public BatchKey(GameplayTag cue, GameObject source, AbilityDefinition ability)
            {
                this.cue = cue;
                this.source = source;
                this.ability = ability;
            }

            public bool Equals(BatchKey other)
            {
                return cue == other.cue && source == other.source && ability == other.ability;
            }

            public override bool Equals(object obj) => obj is BatchKey other && Equals(other);

            public override int GetHashCode() => cue.GetHashCode();
        }
    }
}
