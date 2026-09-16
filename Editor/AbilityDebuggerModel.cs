using System.Collections.Generic;
using Fofuxo.GameplayAbilitySystem;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem.Editor
{
    /// <summary>
    /// The readings the Ability Debugger window shows, computed away from IMGUI so
    /// each one is pinned by a test instead of by a screenshot. The window owns
    /// layout and nothing else; every number it prints comes from here, and every
    /// verdict it prints comes from the runtime.
    /// </summary>
    internal static class AbilityDebuggerModel
    {
        /// <summary>One granted ability and what the runtime would say to activating it now.</summary>
        internal readonly struct ActivationReading
        {
            public ActivationReading(AbilityDefinition ability, AbilityActivationResult result)
            {
                Ability = ability;
                Result = result;
            }

            public AbilityDefinition Ability { get; }
            public AbilityActivationResult Result { get; }
        }

        /// <summary>Kind names in enum order, for the history filter mask.</summary>
        public static readonly string[] KindNames = System.Enum.GetNames(typeof(AbilityEventKind));

        public const int AllKinds = -1;

        /// <summary>Every ability system in the loaded scenes, inactive ones included, by name.</summary>
        public static void CollectActors(List<AbilitySystem> into)
        {
            into.Clear();
            into.AddRange(Object.FindObjectsByType<AbilitySystem>(
                FindObjectsInactive.Include, FindObjectsSortMode.None));
            into.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        }

        /// <summary>
        /// Runs the runtime's own <see cref="AbilitySystem.EvaluateActivation"/>
        /// for every granted ability, against a context built the way a caller
        /// would build it. The window has no activation rules of its own: what it
        /// prints is exactly what <c>TryActivate</c> would have said.
        /// </summary>
        public static void AuditActivations(
            AbilitySystem system, GameObject target, List<ActivationReading> into)
        {
            into.Clear();
            if (system == null || system.Loadout == null)
            {
                return;
            }

            AbilityContext context = AbilityContext.FromTarget(system.gameObject, target);
            IReadOnlyList<AbilityDefinition> abilities = system.Loadout.Abilities;
            for (int i = 0; i < abilities.Count; i++)
            {
                AbilityDefinition ability = abilities[i];
                if (ability != null)
                {
                    into.Add(new ActivationReading(ability, system.EvaluateActivation(ability, context)));
                }
            }
        }

        public static bool PassesFilter(in AbilityEvent recorded, int kindMask)
        {
            return (kindMask & (1 << (int)recorded.Kind)) != 0;
        }

        /// <summary>
        /// One history row: how long ago, on which frame, what. The age is
        /// against the caller's clock so a paused Editor keeps its rows still.
        /// </summary>
        public static string FormatEvent(in AbilityEvent recorded, float now)
        {
            float age = Mathf.Max(0f, now - recorded.Time);
            return $"-{age,7:0.000}s  f{recorded.Frame}  {recorded.Kind}  {recorded.Describe()}";
        }

        public static string DescribeInstance(AbilityInstance instance)
        {
            if (instance == null)
            {
                return "(none)";
            }

            string id = instance.Definition == null ? "<none>" : instance.Definition.AbilityId;
            if (instance.Timeline == null)
            {
                return $"{id} · no timeline · {instance.ElapsedTime:0.00} s";
            }

            return $"{id} · step {instance.StepIndex + 1}/{instance.Timeline.StepCount} · " +
                   $"{instance.CurrentPhase} · frame {instance.CurrentFrame} · " +
                   $"{instance.ElapsedTime:0.00} s";
        }

        public static string DescribeEffect(ActiveGameplayEffect effect)
        {
            if (effect == null)
            {
                return "(none)";
            }

            string name = effect.Definition == null ? "<effect>" : effect.Definition.name;
            string stacks = effect.StackCount > 1 ? $" ×{effect.StackCount}" : string.Empty;
            string duration = effect.IsInfinite
                ? "infinite"
                : $"{effect.RemainingDuration:0.00}/{effect.TotalDuration:0.00} s";
            string period = effect.IsPeriodic
                ? $" · period {effect.Period:0.00} s ({effect.PeriodCount} ran)"
                : string.Empty;
            string source = effect.Source == null ? string.Empty : $" · from {effect.Source.name}";
            return $"{name}{stacks} · {duration}{period}{source}";
        }

        public static string DescribeCue(ActiveGameplayCue cue)
        {
            if (cue == null)
            {
                return "(none)";
            }

            GameplayCueParameters parameters = cue.Parameters;
            string owner = parameters.Effect != null
                ? parameters.Effect.name
                : parameters.Ability != null ? parameters.Ability.AbilityId : "code";
            string stacks = parameters.StackCount > 1 ? $" ×{parameters.StackCount}" : string.Empty;
            string source = parameters.Source != null && parameters.Source != parameters.Owner
                ? $" · from {parameters.Source.name}"
                : string.Empty;
            return $"{parameters.Cue.Value}{stacks} · {cue.Handle} · {owner}{source}";
        }

        public static string DescribeAttribute(AttributeValue value)
        {
            if (value == null)
            {
                return "(none)";
            }

            string limits = float.IsPositiveInfinity(value.MaxValue)
                ? $"≥ {value.MinValue:0.##}"
                : $"{value.MinValue:0.##}..{value.MaxValue:0.##}";
            string modifiers = value.ModifierCount == 0
                ? string.Empty
                : $" · {value.ModifierCount} modifier(s)";
            return $"{value.CurrentValue:0.##} (base {value.BaseValue:0.##}, {limits}){modifiers}";
        }

        /// <summary>Cooldown and charges for one granted ability, as the runtime reports them.</summary>
        public static string DescribeReadiness(AbilitySystem system, AbilityDefinition ability)
        {
            if (system == null || ability == null)
            {
                return "(none)";
            }

            string cooldown = system.IsOnCooldown(ability)
                ? $"cooldown {system.GetCooldownRemaining(ability):0.00} s"
                : "ready";
            string charges = ability.HasLimitedCharges
                ? $" · charges {system.GetCharges(ability):0.##}/{ability.MaxCharges}"
                : string.Empty;
            return cooldown + charges;
        }

        public static string DescribeTags(AbilitySystem system)
        {
            if (system == null)
            {
                return "(none)";
            }

            string joined = string.Empty;
            foreach (GameplayTag tag in system.ActiveTags)
            {
                joined += joined.Length == 0 ? tag.Value : ", " + tag.Value;
            }

            return joined.Length == 0 ? "(none)" : joined;
        }
    }
}
