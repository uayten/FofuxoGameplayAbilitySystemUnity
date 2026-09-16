using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// Everything a presenter may know about one cue, typed: who raised it, on
    /// whom, from which ability or effect, how hard, where and which way.
    /// Read-only by construction — no setter, no runtime object — so a
    /// presenter can present and nothing else. A test pins that shape.
    /// </summary>
    public readonly struct GameplayCueParameters
    {
        public GameplayCueParameters(
            GameplayTag cue,
            GameplayCueEvent cueEvent,
            GameplayCueOutcome outcome,
            GameplayCueHandle handle,
            GameObject owner,
            GameObject source,
            AbilityDefinition ability,
            GameplayEffectDefinition effect,
            int level,
            float magnitude,
            int stackCount,
            in AbilityContext context,
            in AbilityTargetHit hit,
            Vector3 location,
            Vector3 normal)
        {
            Cue = cue;
            Event = cueEvent;
            Outcome = outcome;
            Handle = handle;
            Owner = owner;
            Source = source;
            Ability = ability;
            Effect = effect;
            Level = level;
            Magnitude = magnitude;
            StackCount = stackCount;
            Context = context;
            Hit = hit;
            Location = location;
            Normal = normal;
        }

        /// <summary>The cue tag, after any replacement or filter rewrote it.</summary>
        public GameplayTag Cue { get; }

        public GameplayCueEvent Event { get; }

        public GameplayCueOutcome Outcome { get; }

        /// <summary>Stable across Add, WhileActive and Remove of one persistent cue.</summary>
        public GameplayCueHandle Handle { get; }

        /// <summary>The actor the cue is presented on: the target of an effect, the owner of a step.</summary>
        public GameObject Owner { get; }

        /// <summary>Who caused it: the attacker behind an effect, the owner itself for a step cue.</summary>
        public GameObject Source { get; }

        /// <summary>The ability involved, or null for an effect applied outside one.</summary>
        public AbilityDefinition Ability { get; }

        /// <summary>The effect that raised it, or null for a step or manual cue.</summary>
        public GameplayEffectDefinition Effect { get; }

        public int Level { get; }

        /// <summary>
        /// How hard: the damage dealt, the first modifier's magnitude, or the
        /// level when nothing better exists. Zero for a step or manual cue.
        /// </summary>
        public float Magnitude { get; }

        /// <summary>Stacks of the persistent effect behind the cue; one otherwise.</summary>
        public int StackCount { get; }

        /// <summary>The activation context the cue was raised from.</summary>
        public AbilityContext Context { get; }

        /// <summary>The contact the cue is about, when a query produced one.</summary>
        public AbilityTargetHit Hit { get; }

        public bool HasHit => Hit.IsValid;

        /// <summary>World position: the contact point when there is one, else the owner's position.</summary>
        public Vector3 Location { get; }

        /// <summary>Surface normal at the contact, or up when there is none.</summary>
        public Vector3 Normal { get; }

        /// <summary>Planar direction of the action, from the context.</summary>
        public Vector3 Direction => Context.Direction;

        public bool IsPersistent => Event != GameplayCueEvent.Execute;

        /// <summary>
        /// A copy presenting a different cue tag, for a filter or a
        /// replacement.
        /// </summary>
        public GameplayCueParameters WithCue(GameplayTag cue)
        {
            return new GameplayCueParameters(
                cue, Event, Outcome, Handle, Owner, Source, Ability, Effect, Level,
                Magnitude, StackCount, Context, Hit, Location, Normal);
        }

        /// <summary>A copy for another moment of the same cue's life, under a handle.</summary>
        public GameplayCueParameters WithEvent(GameplayCueEvent cueEvent, GameplayCueHandle handle)
        {
            return new GameplayCueParameters(
                Cue, cueEvent, Outcome, handle, Owner, Source, Ability, Effect, Level,
                Magnitude, StackCount, Context, Hit, Location, Normal);
        }

        /// <summary>A copy carrying a different outcome.</summary>
        public GameplayCueParameters WithOutcome(GameplayCueOutcome outcome)
        {
            return new GameplayCueParameters(
                Cue, Event, outcome, Handle, Owner, Source, Ability, Effect, Level,
                Magnitude, StackCount, Context, Hit, Location, Normal);
        }

        /// <summary>A copy carrying a different stack count.</summary>
        public GameplayCueParameters WithStackCount(int stackCount)
        {
            return new GameplayCueParameters(
                Cue, Event, Outcome, Handle, Owner, Source, Ability, Effect, Level,
                Magnitude, stackCount, Context, Hit, Location, Normal);
        }

        /// <summary>A copy carrying a different magnitude.</summary>
        public GameplayCueParameters WithMagnitude(float magnitude)
        {
            return new GameplayCueParameters(
                Cue, Event, Outcome, Handle, Owner, Source, Ability, Effect, Level,
                magnitude, StackCount, Context, Hit, Location, Normal);
        }

        /// <summary>A copy placed at another point, with the surface normal there.</summary>
        public GameplayCueParameters WithLocation(Vector3 location, Vector3 normal)
        {
            return new GameplayCueParameters(
                Cue, Event, Outcome, Handle, Owner, Source, Ability, Effect, Level,
                Magnitude, StackCount, Context, Hit, location, normal);
        }

        /// <summary>
        /// A cue raised from an ability activation: a step trigger, or a
        /// derived ability's own code. Located at the activation's committed
        /// target when it has one, else at the owner.
        /// </summary>
        public static GameplayCueParameters ForAbility(
            GameplayTag cue,
            AbilityInstance instance,
            GameObject owner,
            GameplayCueOutcome outcome = GameplayCueOutcome.Landed)
        {
            AbilityTargetHit hit = default;
            if (instance != null && instance.TargetData.Count > 0)
            {
                hit = instance.TargetData[0];
            }

            Vector3 location = hit.IsValid
                ? hit.Point
                : owner != null ? owner.transform.position : Vector3.zero;
            AbilityContext context = instance?.Context ?? default;
            return new GameplayCueParameters(
                cue,
                GameplayCueEvent.Execute,
                outcome,
                GameplayCueHandle.None,
                owner,
                owner,
                instance?.Definition,
                null,
                1,
                0f,
                1,
                in context,
                in hit,
                location,
                hit.IsValid ? hit.Normal : Vector3.up);
        }

        /// <summary>
        /// A cue raised by an effect application, presented on the target with
        /// the attacker as source and the contact the application landed on.
        /// </summary>
        public static GameplayCueParameters ForEffect(
            GameplayTag cue,
            GameplayEffectSpec spec,
            GameplayCueOutcome outcome,
            float magnitude,
            int stackCount = 1)
        {
            AbilityTargetHit hit = spec.HasHit ? spec.Hit : default;
            Vector3 location = hit.IsValid
                ? hit.Point
                : spec.Target != null ? spec.Target.transform.position : Vector3.zero;
            AbilityContext context = spec.Context;
            return new GameplayCueParameters(
                cue,
                GameplayCueEvent.Execute,
                outcome,
                GameplayCueHandle.None,
                spec.Target,
                spec.Source,
                spec.SourceAbility,
                spec.Definition,
                spec.Level,
                magnitude,
                stackCount,
                in context,
                in hit,
                location,
                hit.IsValid ? hit.Normal : Vector3.up);
        }

        /// <summary>
        /// A cue raised from code with nothing but a context: an AI tell, a
        /// successful parry. Located at the context's target when it has one.
        /// </summary>
        public static GameplayCueParameters ForContext(
            GameplayTag cue,
            in AbilityContext context,
            AbilityDefinition ability = null,
            GameplayCueOutcome outcome = GameplayCueOutcome.Landed)
        {
            GameObject owner = context.Owner;
            Vector3 location = context.Target != null
                ? context.Target.transform.position
                : owner != null ? owner.transform.position : context.AimPoint;
            return new GameplayCueParameters(
                cue,
                GameplayCueEvent.Execute,
                outcome,
                GameplayCueHandle.None,
                owner,
                owner,
                ability,
                null,
                1,
                0f,
                1,
                in context,
                default,
                location,
                Vector3.up);
        }

        public override string ToString()
        {
            string what = Effect != null
                ? Effect.name
                : Ability != null ? Ability.AbilityId : "manual";
            return $"{Cue} {Event} {Outcome} {Handle} from {what}" +
                   (StackCount > 1 ? $" ×{StackCount}" : string.Empty);
        }
    }
}
