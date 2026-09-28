using System.Collections.Generic;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// Turns the identifiers in a record back into the assets they were taken
    /// from. This is the adapter the package refuses to guess: a game knows
    /// where its abilities and effects live, and the package only knows their
    /// ids.
    /// </summary>
    public interface IAbilitySaveResolver
    {
        /// <summary>The ability with this id, or null when it no longer exists.</summary>
        AbilityDefinition ResolveAbility(string abilityId);

        /// <summary>The effect with this id, or null when it no longer exists.</summary>
        GameplayEffectDefinition ResolveEffect(string effectId);
    }

    /// <summary>
    /// The resolver a single-actor save needs and nothing more: the actor's own
    /// loadout, plus every effect authored inside those abilities. A game whose
    /// effects live elsewhere - a buff granted by an item, an effect from a
    /// catalog asset - registers them with AddEffects or writes its own
    /// <see cref="IAbilitySaveResolver"/>.
    /// </summary>
    public sealed class AbilitySaveResolver : IAbilitySaveResolver
    {
        private readonly Dictionary<string, AbilityDefinition> abilities = new();
        private readonly Dictionary<string, GameplayEffectDefinition> effects = new();

        /// <summary>Builds a resolver from one actor's loadout and its embedded effects.</summary>
        public AbilitySaveResolver(AbilitySystem system)
        {
            if (system == null || system.Loadout == null)
            {
                return;
            }

            AddAbilities(system.Loadout.Abilities);
            AddEffects(system.Loadout.GrantedEffects);
        }

        /// <summary>Builds a resolver from an explicit set of assets.</summary>
        public AbilitySaveResolver(
            IEnumerable<AbilityDefinition> abilityDefinitions,
            IEnumerable<GameplayEffectDefinition> effectDefinitions = null)
        {
            AddAbilities(abilityDefinitions);
            AddEffects(effectDefinitions);
        }

        /// <summary>
        /// Registers abilities by id, and every effect authored on their steps
        /// with them: an embedded effect has no other way of being found.
        /// </summary>
        public AbilitySaveResolver AddAbilities(IEnumerable<AbilityDefinition> abilityDefinitions)
        {
            if (abilityDefinitions == null)
            {
                return this;
            }

            foreach (AbilityDefinition ability in abilityDefinitions)
            {
                if (ability == null || string.IsNullOrWhiteSpace(ability.AbilityId))
                {
                    continue;
                }

                abilities[ability.AbilityId] = ability;
                AddEmbeddedEffects(ability);
            }

            return this;
        }

        /// <summary>Registers effects that live outside any ability.</summary>
        public AbilitySaveResolver AddEffects(IEnumerable<GameplayEffectDefinition> effectDefinitions)
        {
            if (effectDefinitions == null)
            {
                return this;
            }

            foreach (GameplayEffectDefinition effect in effectDefinitions)
            {
                Register(effect);
            }

            return this;
        }

        /// <summary>The registered ability with this id, or null.</summary>
        public AbilityDefinition ResolveAbility(string abilityId)
        {
            return !string.IsNullOrWhiteSpace(abilityId) &&
                   abilities.TryGetValue(abilityId, out AbilityDefinition ability)
                ? ability
                : null;
        }

        /// <summary>The registered effect with this id, or null.</summary>
        public GameplayEffectDefinition ResolveEffect(string effectId)
        {
            return !string.IsNullOrWhiteSpace(effectId) &&
                   effects.TryGetValue(effectId, out GameplayEffectDefinition effect)
                ? effect
                : null;
        }

        private void AddEmbeddedEffects(AbilityDefinition ability)
        {
            // Cooldowns are effects now, and so are an ability's cost and
            // the effects it holds while active.
            Register(ability.CooldownGameplayEffect);
            Register(ability.CostGameplayEffect);
            foreach (GameplayEffectDefinition active in ability.ActiveEffects)
            {
                Register(active);
            }

            if (ability is not TimelineAbilityDefinition timeline)
            {
                return;
            }

            foreach (AbilityStep step in timeline.Steps)
            {
                if (step == null)
                {
                    continue;
                }

                foreach (AbilityEffectTrigger trigger in step.EffectTriggers)
                {
                    Register(trigger.Effect);
                }
            }
        }

        private void Register(GameplayEffectDefinition effect)
        {
            if (effect != null && !string.IsNullOrWhiteSpace(effect.EffectId))
            {
                effects[effect.EffectId] = effect;
            }
        }
    }
}
