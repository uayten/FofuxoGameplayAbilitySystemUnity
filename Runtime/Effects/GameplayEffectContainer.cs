using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// Every gameplay effect live on one actor. It owns the whole lifecycle —
    /// application gating, instant execution, duration, periods, stacking,
    /// overflow, immunity, granted tags and removal — and it is the only place
    /// that attaches an attribute modifier with a lifetime.
    ///
    /// It resolves the actor <see cref="AttributeSet"/> for the numbers and the
    /// actor <see cref="AbilitySystem"/> for the tags. Neither is required: an
    /// actor with no attribute set still receives tag-only effects, and an actor
    /// with no ability system still answers <see cref="HasTag"/> for the tags
    /// its own effects granted.
    ///
    /// The component is added on demand by <see cref="For"/>, so nothing has to
    /// be wired on a prefab for an effect to land — active effects are runtime
    /// state, and runtime state is never authored.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameplayEffectContainer : MonoBehaviour
    {
        private readonly List<ActiveGameplayEffect> activeEffects = new();
        private readonly Dictionary<GameplayTag, int> grantedTagCounts = new();
        private readonly Dictionary<GameplayTag, int> immunityTagCounts = new();
        /// <summary>Ticked over a copy: an expiring effect may apply or remove another.</summary>
        private readonly List<ActiveGameplayEffect> tickBuffer = new();
        private readonly List<ActiveGameplayEffect> removalBuffer = new();
        private AttributeSet attributeSet;
        private AbilitySystem abilitySystem;
        private bool tearingDown;

        /// <summary>Raised when a duration or infinite effect becomes active.</summary>
        public event Action<ActiveGameplayEffect> EffectApplied;

        /// <summary>Raised when an active effect gains or loses a stack.</summary>
        public event Action<ActiveGameplayEffect> EffectStackChanged;

        /// <summary>Raised when an active effect expires or is removed.</summary>
        public event Action<ActiveGameplayEffect> EffectRemoved;

        /// <summary>
        /// Raised when application tags or an active immunity refused a spec.
        /// This is the seam a parry or an armor rule hangs on: the effect never
        /// landed, and the listener knows exactly what was stopped.
        /// </summary>
        public event Action<GameplayEffectSpec> EffectBlocked;

        /// <summary>Every effect live on this actor, oldest first.</summary>
        public IReadOnlyList<ActiveGameplayEffect> ActiveEffects => activeEffects;

        public int ActiveEffectCount => activeEffects.Count;

        public AttributeSet Attributes
        {
            get
            {
                Resolve();
                return attributeSet;
            }
        }

        /// <summary>
        /// The container on this actor, created if it has none. Returns null
        /// only for a null or destroyed actor, so callers never add a component
        /// to something on its way out.
        /// </summary>
        public static GameplayEffectContainer For(GameObject actor)
        {
            if (actor == null)
            {
                return null;
            }

            GameplayEffectContainer container = actor.GetComponent<GameplayEffectContainer>();
            return container != null ? container : actor.AddComponent<GameplayEffectContainer>();
        }

        /// <summary>The container on this actor, or null when it has none. Never adds one.</summary>
        public static GameplayEffectContainer Find(GameObject actor)
        {
            return actor != null ? actor.GetComponent<GameplayEffectContainer>() : null;
        }

        private void Awake()
        {
            Resolve();
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        private void OnDestroy()
        {
            tearingDown = true;
            RemoveAllEffects();
        }

        /// <summary>
        /// Applies one spec to this actor. Every path through the lifecycle
        /// starts here: gating, capture, instant execution, activation,
        /// stacking, and overflow.
        /// </summary>
        public GameplayEffectApplicationResult Apply(GameplayEffectSpec spec)
        {
            GameplayEffectApplicationResult result;
            using (AbilityDiagnostics.EffectApplicationMarker.Auto())
            using (AbilityDiagnostics.EffectApplications.Begin())
            {
                result = ApplyUnsampled(spec);
            }

            if (AbilityDiagnostics.Enabled && spec != null && spec.Definition != null)
            {
                RecordApplication(spec, in result);
            }

            return result;
        }

        private GameplayEffectApplicationResult ApplyUnsampled(GameplayEffectSpec spec)
        {
            if (spec == null || spec.Definition == null)
            {
                return GameplayEffectApplicationResult.Failed(
                    GameplayEffectApplicationOutcome.None);
            }

            GameplayEffectDefinition definition = spec.Definition;
            if (!CanApply(definition))
            {
                EffectBlocked?.Invoke(spec);
                RaiseRefusalCue(spec);
                return GameplayEffectApplicationResult.Failed(
                    GameplayEffectApplicationOutcome.Blocked);
            }

            Resolve();
            spec.CaptureSnapshots();
            spec.CalculateMagnitudes();

            if (!definition.ExecuteEffect(spec, false))
            {
                return GameplayEffectApplicationResult.Failed(
                    GameplayEffectApplicationOutcome.Rejected);
            }

            RemoveEffectsWithTags(definition.RemoveEffectsWithTags);

            if (definition.DurationPolicy == GameplayEffectDurationPolicy.Instant)
            {
                ApplyInstantModifiers(spec, 1);
                RaiseExecuteCue(spec);
                return new GameplayEffectApplicationResult(
                    GameplayEffectApplicationOutcome.Executed);
            }

            ActiveGameplayEffect existing = FindStack(spec);
            return existing != null ? Stack(existing, spec) : Activate(spec);
        }

        /// <summary>
        /// Applies an effect with no asset behind it: a timed attribute change
        /// decided in code, such as a stun whose duration is authored on the
        /// attack that caused it. The shared definition it borrows carries no
        /// state, so nothing is written to an asset.
        /// </summary>
        public GameplayEffectApplicationResult ApplyDynamic(
            GameplayEffectDurationPolicy policy,
            GameplayAttribute attribute,
            AttributeOperation operation,
            float magnitude,
            float durationSeconds = 0f,
            EffectStacking stacking = EffectStacking.Refresh,
            GameObject source = null,
            float periodSeconds = 0f)
        {
            GameplayEffectDefinition definition =
                GameplayEffectDefinition.Dynamic(policy, stacking);
            GameplayEffectSpec spec = new GameplayEffectSpec(definition, source, gameObject)
                .AddDynamicModifier(attribute, operation, magnitude)
                .SetDuration(durationSeconds)
                .SetPeriod(periodSeconds);
            return Apply(spec);
        }

        /// <summary>
        /// Whether an effect would be accepted right now. Side-effect free, so
        /// a caller may ask before committing to a hit.
        /// </summary>
        public bool CanApply(GameplayEffectDefinition definition)
        {
            if (definition == null)
            {
                return false;
            }

            IReadOnlyList<GameplayTag> required = definition.ApplicationRequiredTags;
            for (int i = 0; i < required.Count; i++)
            {
                if (!HasTag(required[i]))
                {
                    return false;
                }
            }

            IReadOnlyList<GameplayTag> blocked = definition.ApplicationBlockedTags;
            for (int i = 0; i < blocked.Count; i++)
            {
                if (HasTag(blocked[i]))
                {
                    return false;
                }
            }

            // The rules of the effect's kind — damage refuses a dead, invulnerable
            // or parrying target whatever the asset lists — after the authored ones.
            if (definition.IsBlockedOn(this))
            {
                return false;
            }

            return !IsImmuneTo(definition);
        }

        /// <summary>
        /// True when an active effect grants immunity to one of this effect
        /// tags. Immunity is named by tag, so an effect becomes immune to a
        /// whole category without either side knowing the other.
        /// </summary>
        public bool IsImmuneTo(GameplayEffectDefinition definition)
        {
            if (definition == null || immunityTagCounts.Count == 0)
            {
                return false;
            }

            IReadOnlyList<GameplayTag> tags = definition.EffectTags;
            for (int i = 0; i < tags.Count; i++)
            {
                if (immunityTagCounts.TryGetValue(tags[i], out int count) && count > 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Tags this actor holds: the ones effects granted, plus everything the
        /// ability system knows about when the actor has one.
        /// </summary>
        public bool HasTag(GameplayTag tag)
        {
            if (tag.IsEmpty)
            {
                return false;
            }

            if (grantedTagCounts.TryGetValue(tag, out int count) && count > 0)
            {
                return true;
            }

            Resolve();
            return abilitySystem != null && abilitySystem.HasTag(tag);
        }

        /// <summary>True while an effect on this actor grants the tag.</summary>
        public bool HasEffectTag(GameplayTag tag)
        {
            return !tag.IsEmpty &&
                   grantedTagCounts.TryGetValue(tag, out int count) &&
                   count > 0;
        }

        /// <summary>True while the handle names an effect that is still live here.</summary>
        public bool IsActive(GameplayEffectHandle handle)
        {
            return TryGetActiveEffect(handle, out _);
        }

        public bool TryGetActiveEffect(
            GameplayEffectHandle handle, out ActiveGameplayEffect effect)
        {
            effect = null;
            if (!handle.IsValid)
            {
                return false;
            }

            for (int i = 0; i < activeEffects.Count; i++)
            {
                if (activeEffects[i].Handle == handle)
                {
                    effect = activeEffects[i];
                    return true;
                }
            }

            return false;
        }

        /// <summary>Stacks on the effect the handle names, or zero when it is gone.</summary>
        public int GetStackCount(GameplayEffectHandle handle)
        {
            return TryGetActiveEffect(handle, out ActiveGameplayEffect effect)
                ? effect.StackCount
                : 0;
        }

        /// <summary>Seconds left on the effect the handle names, or zero when it is gone.</summary>
        public float GetRemainingDuration(GameplayEffectHandle handle)
        {
            return TryGetActiveEffect(handle, out ActiveGameplayEffect effect)
                ? effect.RemainingDuration
                : 0f;
        }

        /// <summary>
        /// Restarts the duration of the effect the handle names, recalculating
        /// its magnitudes from the source as they stand now.
        /// </summary>
        public bool TryRefresh(GameplayEffectHandle handle)
        {
            if (!TryGetActiveEffect(handle, out ActiveGameplayEffect effect))
            {
                return false;
            }

            effect.Spec.CalculateMagnitudes();
            effect.RefreshDuration();
            RewriteModifierSlots(effect);
            return true;
        }

        /// <summary>Removes the effect the handle names. False when it was already gone.</summary>
        public bool TryRemove(GameplayEffectHandle handle)
        {
            if (!TryGetActiveEffect(handle, out ActiveGameplayEffect effect))
            {
                return false;
            }

            Remove(effect);
            return true;
        }

        /// <summary>
        /// Removes one stack from the effect the handle names, removing the
        /// whole effect when the last stack goes.
        /// </summary>
        public bool TryRemoveStack(GameplayEffectHandle handle, int stacks = 1)
        {
            if (stacks <= 0 || !TryGetActiveEffect(handle, out ActiveGameplayEffect effect))
            {
                return false;
            }

            if (effect.StackCount <= stacks)
            {
                Remove(effect);
                return true;
            }

            effect.StackCount -= stacks;
            RewriteModifierSlots(effect);
            RefreshPersistentCue(effect);
            EffectStackChanged?.Invoke(effect);
            return true;
        }

        /// <summary>Removes every active effect carrying any of these tags.</summary>
        public int RemoveEffectsWithTags(IReadOnlyList<GameplayTag> tags)
        {
            if (tags == null || tags.Count == 0 || activeEffects.Count == 0)
            {
                return 0;
            }

            removalBuffer.Clear();
            for (int i = 0; i < activeEffects.Count; i++)
            {
                ActiveGameplayEffect effect = activeEffects[i];
                if (effect.Definition != null && effect.Definition.HasAnyEffectTag(tags))
                {
                    removalBuffer.Add(effect);
                }
            }

            return RemoveBuffered();
        }

        /// <summary>Removes every active effect applied by this source.</summary>
        public int RemoveEffectsFromSource(GameObject source)
        {
            removalBuffer.Clear();
            for (int i = 0; i < activeEffects.Count; i++)
            {
                if (activeEffects[i].Source == source)
                {
                    removalBuffer.Add(activeEffects[i]);
                }
            }

            return RemoveBuffered();
        }

        /// <summary>Removes every active effect built from this definition.</summary>
        public int RemoveEffects(GameplayEffectDefinition definition)
        {
            if (definition == null)
            {
                return 0;
            }

            removalBuffer.Clear();
            for (int i = 0; i < activeEffects.Count; i++)
            {
                if (activeEffects[i].Definition == definition)
                {
                    removalBuffer.Add(activeEffects[i]);
                }
            }

            return RemoveBuffered();
        }

        public int RemoveAllEffects()
        {
            removalBuffer.Clear();
            removalBuffer.AddRange(activeEffects);
            return RemoveBuffered();
        }

        /// <summary>
        /// Ages every active effect. Called automatically; public so tests step
        /// time deterministically. Periods run before expiry, so the last tick
        /// of a three-second, one-second effect is not lost to rounding.
        /// </summary>
        public void Tick(float deltaTime)
        {
            float step = Mathf.Max(0f, deltaTime);
            if (step <= 0f || activeEffects.Count == 0)
            {
                return;
            }

            tickBuffer.Clear();
            tickBuffer.AddRange(activeEffects);
            for (int i = 0; i < tickBuffer.Count; i++)
            {
                ActiveGameplayEffect effect = tickBuffer[i];
                if (!activeEffects.Contains(effect))
                {
                    continue;
                }

                TickEffect(effect, step);
            }
        }

        private void TickEffect(ActiveGameplayEffect effect, float step)
        {
            if (effect.Spec.HasLiveMagnitudes)
            {
                effect.Spec.CalculateMagnitudes();
                RewriteModifierSlots(effect);
            }

            if (effect.IsPeriodic)
            {
                float consumed = effect.IsInfinite
                    ? step
                    : Mathf.Min(step, effect.RemainingDuration);
                effect.PeriodAccumulator += consumed;
                while (effect.PeriodAccumulator >= effect.Period && effect.Period > 0f)
                {
                    effect.PeriodAccumulator -= effect.Period;
                    ExecutePeriod(effect);
                    if (!activeEffects.Contains(effect))
                    {
                        return;
                    }
                }
            }

            if (effect.IsInfinite)
            {
                return;
            }

            effect.RemainingDuration -= step;
            if (effect.RemainingDuration <= 0f)
            {
                effect.RemainingDuration = 0f;
                Remove(effect);
            }
        }

        private void ExecutePeriod(ActiveGameplayEffect effect)
        {
            effect.PeriodCount++;
            if (effect.Definition != null)
            {
                effect.Definition.ExecuteEffect(effect.Spec, true);
            }

            ApplyInstantModifiers(effect.Spec, effect.StackCount);
        }

        private GameplayEffectApplicationResult Activate(GameplayEffectSpec spec)
        {
            GameplayEffectHandle handle = GameplayEffectHandle.Next();
            ActiveGameplayEffect effect = new(handle, spec);
            activeEffects.Add(effect);

            if (!effect.IsPeriodic)
            {
                RewriteModifierSlots(effect);
            }
            else if (spec.Definition.ExecutePeriodOnApplication)
            {
                ExecutePeriod(effect);
            }

            GrantTags(effect);
            AddPersistentCue(effect);
            EffectApplied?.Invoke(effect);
            return new GameplayEffectApplicationResult(
                GameplayEffectApplicationOutcome.Applied, handle);
        }

        /// <summary>
        /// Puts an effect back exactly as a record found it, without applying
        /// it again. The difference from <see cref="Apply"/> is the whole point:
        /// no gating, no <c>ExecuteEffect</c> and no period run, so a saved
        /// poison comes back poisoning rather than dealing its first tick a
        /// second time. Modifiers, granted tags and the persistent cue are
        /// rebuilt, because those are what the effect is while it lives.
        /// </summary>
        internal ActiveGameplayEffect RestoreActiveEffect(
            GameplayEffectSpec spec,
            float totalDuration,
            float remainingDuration,
            bool infinite,
            int stackCount,
            float periodAccumulator,
            int periodCount)
        {
            if (spec == null || spec.Definition == null)
            {
                return null;
            }

            Resolve();
            spec.CaptureSnapshots();
            spec.CalculateMagnitudes();

            ActiveGameplayEffect effect = new(GameplayEffectHandle.Next(), spec)
            {
                StackCount = Mathf.Max(1, stackCount),
                TotalDuration = infinite ? float.PositiveInfinity : totalDuration,
                RemainingDuration = infinite ? float.PositiveInfinity : remainingDuration,
                PeriodAccumulator = periodAccumulator,
                PeriodCount = periodCount
            };

            activeEffects.Add(effect);
            if (!effect.IsPeriodic)
            {
                RewriteModifierSlots(effect);
            }

            GrantTags(effect);
            AddPersistentCue(effect);
            EffectApplied?.Invoke(effect);
            return effect;
        }

        private GameplayEffectApplicationResult Stack(
            ActiveGameplayEffect effect, GameplayEffectSpec spec)
        {
            GameplayEffectStacking stacking = spec.Definition.Stacking;
            switch (stacking.Policy)
            {
                case EffectStacking.Ignore:
                    return new GameplayEffectApplicationResult(
                        GameplayEffectApplicationOutcome.Stacked, effect.Handle);

                case EffectStacking.Refresh:
                    effect.AdoptSpec(spec);
                    effect.RefreshDuration();
                    if (stacking.ResetPeriodOnStack)
                    {
                        effect.PeriodAccumulator = 0f;
                    }

                    RewriteModifierSlots(effect);
                    RefreshPersistentCue(effect);
                    EffectStackChanged?.Invoke(effect);
                    return new GameplayEffectApplicationResult(
                        GameplayEffectApplicationOutcome.Stacked, effect.Handle);

                default:
                    return AddStack(effect, spec, stacking);
            }
        }

        private GameplayEffectApplicationResult AddStack(
            ActiveGameplayEffect effect,
            GameplayEffectSpec spec,
            GameplayEffectStacking stacking)
        {
            if (stacking.Limit > 0 && effect.StackCount >= stacking.Limit)
            {
                return Overflow(effect, spec, stacking);
            }

            effect.StackCount++;
            effect.AdoptSpec(spec);
            if (stacking.RefreshDurationOnStack)
            {
                effect.RefreshDuration();
            }

            if (stacking.ResetPeriodOnStack)
            {
                effect.PeriodAccumulator = 0f;
            }

            RewriteModifierSlots(effect);
            RefreshPersistentCue(effect);
            EffectStackChanged?.Invoke(effect);
            return new GameplayEffectApplicationResult(
                GameplayEffectApplicationOutcome.Stacked, effect.Handle);
        }

        private GameplayEffectApplicationResult Overflow(
            ActiveGameplayEffect effect,
            GameplayEffectSpec spec,
            GameplayEffectStacking stacking)
        {
            GameplayEffectDefinition[] overflowEffects = stacking.OverflowEffects;
            for (int i = 0; i < overflowEffects.Length; i++)
            {
                GameplayEffectDefinition overflow = overflowEffects[i];
                if (overflow == null || overflow == spec.Definition)
                {
                    continue;
                }

                Apply(new GameplayEffectSpec(
                    overflow, spec.Source, gameObject, spec.Level));
            }

            if (stacking.ClearStackOnOverflow)
            {
                Remove(effect);
                return new GameplayEffectApplicationResult(
                    GameplayEffectApplicationOutcome.Overflowed);
            }

            if (!stacking.DenyOverflowApplication && stacking.RefreshDurationOnStack)
            {
                effect.RefreshDuration();
            }

            return new GameplayEffectApplicationResult(
                GameplayEffectApplicationOutcome.Overflowed, effect.Handle);
        }

        private ActiveGameplayEffect FindStack(GameplayEffectSpec spec)
        {
            GameplayEffectStacking stacking = spec.Definition.Stacking;
            for (int i = 0; i < activeEffects.Count; i++)
            {
                ActiveGameplayEffect effect = activeEffects[i];
                if (effect.Definition != spec.Definition)
                {
                    continue;
                }

                if (stacking.Scope == GameplayEffectStackScope.ByTarget ||
                    effect.Source == spec.Source)
                {
                    return effect;
                }
            }

            return null;
        }

        /// <summary>
        /// Rewrites the attribute modifier slots this effect owns so there is
        /// exactly one set per stack, each carrying the magnitudes as they
        /// currently evaluate. Adding a slot per stack is what makes additive
        /// stacks accumulate and multiplicative stacks compound, with no rule
        /// per operation.
        /// </summary>
        private void RewriteModifierSlots(ActiveGameplayEffect effect)
        {
            Resolve();
            if (attributeSet == null || effect.IsPeriodic)
            {
                return;
            }

            GameplayEffectSpec spec = effect.Spec;
            int perStack = spec.ModifierCount;
            int wanted = perStack * Mathf.Max(1, effect.StackCount);

            for (int i = effect.modifierSlots.Count - 1; i >= wanted; i--)
            {
                attributeSet.RemoveModifier(effect.modifierSlots[i]);
                effect.modifierSlots.RemoveAt(i);
            }

            for (int i = 0; i < wanted; i++)
            {
                GameplayEffectModifier modifier = spec.GetModifier(i % perStack);
                if (modifier.IsEmpty)
                {
                    continue;
                }

                AttributeModifier attributeModifier = new(
                    modifier.Attribute,
                    modifier.Operation,
                    spec.ModifierMagnitudes[i % perStack],
                    spec.Source);

                if (i < effect.modifierSlots.Count)
                {
                    attributeSet.UpdateModifier(effect.modifierSlots[i], attributeModifier);
                }
                else
                {
                    effect.modifierSlots.Add(attributeSet.AddModifier(attributeModifier));
                }
            }
        }

        private void ApplyInstantModifiers(GameplayEffectSpec spec, int stacks)
        {
            Resolve();
            if (attributeSet == null)
            {
                return;
            }

            int count = spec.ModifierCount;
            for (int stack = 0; stack < Mathf.Max(1, stacks); stack++)
            {
                for (int i = 0; i < count; i++)
                {
                    GameplayEffectModifier modifier = spec.GetModifier(i);
                    if (modifier.IsEmpty)
                    {
                        continue;
                    }

                    attributeSet.ApplyInstantModifier(new AttributeModifier(
                        modifier.Attribute,
                        modifier.Operation,
                        spec.ModifierMagnitudes[i],
                        spec.Source));
                }
            }
        }

        private void GrantTags(ActiveGameplayEffect effect)
        {
            GameplayEffectDefinition definition = effect.Definition;
            if (definition != null)
            {
                AddGrantedTags(effect, definition.GrantedTags);
                AddImmunityTags(definition.GrantedImmunityTags, 1);
            }

            AddGrantedTags(effect, effect.Spec.DynamicGrantedTags);
        }

        private void AddGrantedTags(
            ActiveGameplayEffect effect, IReadOnlyList<GameplayTag> tags)
        {
            for (int i = 0; i < tags.Count; i++)
            {
                GameplayTag tag = tags[i];
                if (tag.IsEmpty)
                {
                    continue;
                }

                grantedTagCounts.TryGetValue(tag, out int count);
                grantedTagCounts[tag] = count + 1;
                effect.grantedTags.Add(tag);
                Resolve();
                abilitySystem?.AddEffectTag(tag);
            }
        }

        private void AddImmunityTags(IReadOnlyList<GameplayTag> tags, int delta)
        {
            for (int i = 0; i < tags.Count; i++)
            {
                GameplayTag tag = tags[i];
                if (tag.IsEmpty)
                {
                    continue;
                }

                immunityTagCounts.TryGetValue(tag, out int count);
                count += delta;
                if (count <= 0)
                {
                    immunityTagCounts.Remove(tag);
                }
                else
                {
                    immunityTagCounts[tag] = count;
                }
            }
        }

        private void ReleaseTags(ActiveGameplayEffect effect)
        {
            for (int i = 0; i < effect.grantedTags.Count; i++)
            {
                GameplayTag tag = effect.grantedTags[i];
                if (grantedTagCounts.TryGetValue(tag, out int count))
                {
                    if (count <= 1)
                    {
                        grantedTagCounts.Remove(tag);
                    }
                    else
                    {
                        grantedTagCounts[tag] = count - 1;
                    }
                }

                // While tearing down, do not go looking for a component: the
                // actor may be half destroyed already. But a system this
                // container has resolved before, and which is still alive, must
                // not be left holding a tag whose effect no longer exists —
                // that is a tag the owner keeps forever.
                if (!tearingDown)
                {
                    Resolve();
                }

                if (abilitySystem != null)
                {
                    abilitySystem.RemoveEffectTag(tag);
                }
            }

            effect.grantedTags.Clear();
            if (effect.Definition != null)
            {
                AddImmunityTags(effect.Definition.GrantedImmunityTags, -1);
            }
        }

        private int RemoveBuffered()
        {
            int removed = 0;
            for (int i = 0; i < removalBuffer.Count; i++)
            {
                if (activeEffects.Contains(removalBuffer[i]))
                {
                    Remove(removalBuffer[i]);
                    removed++;
                }
            }

            removalBuffer.Clear();
            return removed;
        }

        private void Remove(ActiveGameplayEffect effect)
        {
            activeEffects.Remove(effect);
            Resolve();
            if (attributeSet != null)
            {
                for (int i = 0; i < effect.modifierSlots.Count; i++)
                {
                    attributeSet.RemoveModifier(effect.modifierSlots[i]);
                }
            }

            effect.modifierSlots.Clear();
            ReleaseTags(effect);
            RemovePersistentCue(effect);
            if (!tearingDown)
            {
                if (AbilityDiagnostics.Enabled && abilitySystem != null)
                {
                    abilitySystem.Record(AbilityEvent.Removed(effect.Definition, effect.Source));
                }

                EffectRemoved?.Invoke(effect);
            }
        }

        // ------------------------------------------------------------------ cues

        /// <summary>
        /// The cues an effect drives, presented on this actor through its
        /// ability system's dispatcher. An actor with effects and no ability
        /// system presents nothing — a barrel with health has no presenter to
        /// speak of — and an effect that authored no cue raises none.
        /// </summary>
        private GameplayCueDispatcher Cues
        {
            get
            {
                Resolve();
                return abilitySystem != null ? abilitySystem.Cues : null;
            }
        }

        private void RaiseExecuteCue(GameplayEffectSpec spec)
        {
            GameplayCueDispatcher cues = Cues;
            if (cues == null ||
                !spec.Definition.TryResolveCue(GameplayCueOutcome.Landed, out GameplayTag cue))
            {
                return;
            }

            cues.Execute(GameplayCueParameters.ForEffect(
                cue, spec, GameplayCueOutcome.Landed, spec.Definition.ResolveCueMagnitude(spec)));
        }

        /// <summary>
        /// The application was refused. Immunity is this container's own
        /// verdict; every other refusal is classified by the effect, which is
        /// the one that knows a parry from a guard.
        /// </summary>
        private void RaiseRefusalCue(GameplayEffectSpec spec)
        {
            GameplayCueDispatcher cues = Cues;
            if (cues == null)
            {
                return;
            }

            GameplayCueOutcome outcome = IsImmuneTo(spec.Definition)
                ? GameplayCueOutcome.Immune
                : spec.Definition.ClassifyRefusal(this);
            if (spec.Definition.TryResolveCue(outcome, out GameplayTag cue))
            {
                cues.Execute(GameplayCueParameters.ForEffect(cue, spec, outcome, 0f));
            }
        }

        private void AddPersistentCue(ActiveGameplayEffect effect)
        {
            GameplayCueDispatcher cues = Cues;
            if (cues == null ||
                !effect.Definition.TryResolveCue(GameplayCueOutcome.Landed, out GameplayTag cue))
            {
                return;
            }

            effect.CueHandle = cues.Add(
                GameplayCueParameters.ForEffect(
                    cue,
                    effect.Spec,
                    GameplayCueOutcome.Landed,
                    effect.Definition.ResolveCueMagnitude(effect.Spec),
                    effect.StackCount),
                effect.Handle);
        }

        private void RefreshPersistentCue(ActiveGameplayEffect effect)
        {
            if (!effect.CueHandle.IsValid)
            {
                return;
            }

            // The tag is the one the Add decided; Refresh keeps it whatever is
            // passed here, so only the moving parts are rebuilt.
            GameplayCueDispatcher cues = Cues;
            cues?.Refresh(
                effect.CueHandle,
                GameplayCueParameters.ForEffect(
                    default,
                    effect.Spec,
                    GameplayCueOutcome.Landed,
                    effect.Definition.ResolveCueMagnitude(effect.Spec),
                    effect.StackCount));
        }

        private void RemovePersistentCue(ActiveGameplayEffect effect)
        {
            if (!effect.CueHandle.IsValid)
            {
                return;
            }

            GameplayCueDispatcher cues = Cues;
            cues?.Remove(effect.CueHandle);
            effect.CueHandle = GameplayCueHandle.None;
        }

        /// <summary>
        /// The target-side record of an application, in the history of the
        /// ability system sharing this actor. An actor with effects and no
        /// ability system — a barrel with health — has no history to write to.
        /// </summary>
        private void RecordApplication(
            GameplayEffectSpec spec, in GameplayEffectApplicationResult result)
        {
            Resolve();
            if (abilitySystem == null)
            {
                return;
            }

            switch (result.Outcome)
            {
                case GameplayEffectApplicationOutcome.None:
                    return;
                case GameplayEffectApplicationOutcome.Blocked:
                case GameplayEffectApplicationOutcome.Rejected:
                    abilitySystem.Record(
                        AbilityEvent.Blocked(spec.Definition, spec.Source, result.Outcome));
                    return;
                default:
                    abilitySystem.Record(
                        AbilityEvent.Applied(spec.Definition, spec.Source, result.Outcome));
                    return;
            }
        }

        /// <summary>
        /// Finds the attribute set and the ability system, tolerating either one
        /// being added after this container. A missing reference is re-looked-up
        /// rather than cached as absent, so component order never silently
        /// costs an actor its effect tags.
        /// </summary>
        private void Resolve()
        {
            if (attributeSet == null)
            {
                attributeSet = GetComponent<AttributeSet>();
            }

            if (abilitySystem == null)
            {
                abilitySystem = GetComponent<AbilitySystem>();
            }
        }
    }
}
