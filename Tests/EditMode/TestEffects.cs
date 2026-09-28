using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    /// <summary>
    /// The effect assets fixtures build in code now that costs and cooldowns
    /// are effects. Every asset goes into the caller's list, so its TearDown
    /// destroys it with the rest.
    /// </summary>
    internal static class TestEffects
    {
        /// <summary>An Instant cost: subtracts <paramref name="amount"/> from the attribute.</summary>
        public static GameplayEffectDefinition Cost(
            ICollection<Object> owned, GameplayAttribute attribute, float amount)
        {
            GameplayEffectDefinition cost = New(owned, "GE_Cost_Test");
            cost.ConfigureForTests(
                GameplayEffectDurationPolicy.Instant,
                GameplayEffectTargeting.Owner,
                new[] { new GameplayEffectModifier(attribute, AttributeOperation.Add, -amount) });
            return cost;
        }

        /// <summary>A Duration cooldown that grants <paramref name="tag"/> while it lasts.</summary>
        public static GameplayEffectDefinition Cooldown(
            ICollection<Object> owned, float seconds, string tag = "Cooldown.Test", string effectId = null)
        {
            GameplayEffectDefinition cooldown = New(owned, "GE_Cooldown_Test");
            cooldown.ConfigureForTests(
                GameplayEffectDurationPolicy.Duration,
                GameplayEffectTargeting.Owner,
                null,
                seconds);
            cooldown.SetTagsForTests(granted: new[] { new GameplayTag(tag) });
            if (!string.IsNullOrEmpty(effectId))
            {
                cooldown.SetEffectIdForTests(effectId);
            }

            return cooldown;
        }

        /// <summary>An Infinite effect with one modifier, for buffs and drains.</summary>
        public static GameplayEffectDefinition Infinite(
            ICollection<Object> owned,
            GameplayAttribute attribute,
            AttributeOperation operation,
            GameplayEffectMagnitude magnitude,
            float periodSeconds = 0f)
        {
            GameplayEffectDefinition effect = New(owned, "GE_Buff_Test");
            effect.ConfigureForTests(
                GameplayEffectDurationPolicy.Infinite,
                GameplayEffectTargeting.Owner,
                new[] { new GameplayEffectModifier(attribute, operation, magnitude) },
                0f,
                periodSeconds);
            return effect;
        }

        /// <summary>Gives the ability a cooldown of <paramref name="seconds"/>, or none for zero.</summary>
        public static GameplayEffectDefinition SetCooldown(
            ICollection<Object> owned, AbilityDefinition ability, float seconds, string tag = "Cooldown.Test")
        {
            GameplayEffectDefinition cooldown = seconds > 0f ? Cooldown(owned, seconds, tag) : null;
            SetField(ability, "cooldownGameplayEffect", cooldown);
            return cooldown;
        }

        /// <summary>Gives the ability a cost effect, paid again every <paramref name="period"/> when positive.</summary>
        public static GameplayEffectDefinition SetCost(
            ICollection<Object> owned,
            AbilityDefinition ability,
            GameplayAttribute attribute,
            float amount,
            float period = 0f)
        {
            GameplayEffectDefinition cost = Cost(owned, attribute, amount);
            SetField(ability, "costGameplayEffect", cost);
            SetField(ability, "costPeriod", period);
            return cost;
        }

        public static void SetField(object target, string fieldName, object value)
        {
            for (System.Type type = target.GetType(); type != null; type = type.BaseType)
            {
                System.Reflection.FieldInfo field = type.GetField(
                    fieldName,
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    field.SetValue(target, value);
                    return;
                }
            }

            Assert.Fail($"No field named {fieldName}.");
        }

        private static GameplayEffectDefinition New(ICollection<Object> owned, string name)
        {
            GameplayEffectDefinition effect = ScriptableObject.CreateInstance<GameplayEffectDefinition>();
            effect.name = name;
            owned?.Add(effect);
            return effect;
        }
    }
}
