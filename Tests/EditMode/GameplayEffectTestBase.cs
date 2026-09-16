using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    /// <summary>
    /// Shared scaffolding for the gameplay effect tests: object ownership,
    /// actors with attributes, and effect definitions built in code.
    /// </summary>
    public abstract class GameplayEffectTestBase
    {
        protected static readonly GameplayAttribute Health = new("Test.Health");
        protected static readonly GameplayAttribute Strength = new("Test.Strength");
        protected static readonly GameplayAttribute Armor = new("Test.Armor");

        private readonly List<Object> owned = new();

        [TearDown]
        public void DestroyOwnedObjects()
        {
            foreach (Object item in owned)
            {
                if (item != null)
                {
                    Object.DestroyImmediate(item);
                }
            }

            owned.Clear();
        }

        protected T Own<T>(T item) where T : Object
        {
            owned.Add(item);
            return item;
        }

        /// <summary>An actor with an attribute set holding one value.</summary>
        protected GameObject NewActor(
            string name, GameplayAttribute attribute, float value, float max = 100f)
        {
            GameObject actor = Own(new GameObject(name));
            AttributeSet set = actor.AddComponent<AttributeSet>();
            set.SetInitialValues(new[]
            {
                new AttributeSet.InitialValue(attribute, value, 0f, max),
            });
            return actor;
        }

        /// <summary>An actor with an attribute set holding several values.</summary>
        protected GameObject NewActor(string name, params AttributeSet.InitialValue[] initials)
        {
            GameObject actor = Own(new GameObject(name));
            actor.AddComponent<AttributeSet>().SetInitialValues(initials);
            return actor;
        }

        /// <summary>An actor with nothing on it.</summary>
        protected GameObject NewBareActor(string name)
        {
            return Own(new GameObject(name));
        }

        protected static AttributeSet Attributes(GameObject actor)
        {
            return actor.GetComponent<AttributeSet>();
        }

        protected static GameplayEffectContainer Effects(GameObject actor)
        {
            return GameplayEffectContainer.For(actor);
        }

        protected GameplayEffectDefinition NewEffect(
            GameplayEffectDurationPolicy policy,
            GameplayEffectModifier[] modifiers = null,
            float duration = 0f,
            float period = 0f,
            GameplayEffectTargetMode mode = GameplayEffectTargetMode.AbilityTarget)
        {
            GameplayEffectDefinition effect =
                Own(ScriptableObject.CreateInstance<GameplayEffectDefinition>());
            effect.name = "GE_Test_Effect";
            effect.ConfigureForTests(
                policy,
                mode == GameplayEffectTargetMode.Owner
                    ? GameplayEffectTargeting.Owner
                    : GameplayEffectTargeting.AbilityTarget,
                modifiers,
                duration,
                period);
            return effect;
        }

        protected static GameplayEffectModifier Add(GameplayAttribute attribute, float magnitude)
        {
            return new GameplayEffectModifier(attribute, AttributeOperation.Add, magnitude);
        }

        /// <summary>Applies one spec straight to the target container.</summary>
        protected static GameplayEffectApplicationResult Apply(
            GameplayEffectDefinition effect,
            GameObject source,
            GameObject target,
            int level = 1)
        {
            return Effects(target).Apply(
                new GameplayEffectSpec(effect, source, target, level));
        }
    }
}
