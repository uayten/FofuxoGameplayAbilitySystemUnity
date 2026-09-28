using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    /// <summary>
    /// The allocation budget: the calls a game makes every frame, on every
    /// actor, must allocate nothing. These are the paths an AI polls, the tick
    /// an idle actor runs, and the questions a HUD asks - a managed allocation
    /// in any of them is a garbage collection the player feels.
    ///
    /// What is deliberately not here: activation, effect application and target
    /// acquisition. Those allocate, they are supposed to, and
    /// `Documentation~/PERFORMANCE.md` says how much and how to watch them.
    /// </summary>
    public sealed class AllocationBudgetTests
    {
        private static readonly GameplayAttribute Health = new("Test.Health");
        private static readonly GameplayTag Rested = new("State.Rested");

        private GameObject owner;
        private AbilitySystem system;
        private AttributeSet attributes;
        private TimelineAbilityDefinition ability;
        private AbilityLoadout loadout;
        private GameplayEffectDefinition cooldown;
        private bool diagnostics;

        [SetUp]
        public void SetUp()
        {
            diagnostics = AbilityDiagnostics.Enabled;
            owner = new GameObject("BudgetOwner");
            system = owner.AddComponent<AbilitySystem>();
            attributes = owner.AddComponent<AttributeSet>();
            attributes.SetInitialValues(new[]
            {
                new AttributeSet.InitialValue(Health, 100f, 0f, 100f)
            });

            ability = ScriptableObject.CreateInstance<TimelineAbilityDefinition>();
            ability.SetAbilityIdForTests("test.budget");
            SetField(ability, "requiresTarget", false);
            cooldown = TestEffects.SetCooldown(null, ability, 5f);
            AbilityStep step = new();
            step.ConfigureForTests(1, 2, 10, 60f);
            ability.SetStepsForTests(new[] { step });

            loadout = ScriptableObject.CreateInstance<AbilityLoadout>();
            SetField(loadout, "abilities", new AbilityDefinition[] { ability });
            SetField(system, "loadout", loadout);
        }

        [TearDown]
        public void TearDown()
        {
            AbilityDiagnostics.Enabled = diagnostics;
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(ability);
            Object.DestroyImmediate(loadout);
            Object.DestroyImmediate(cooldown);
        }

        [Test]
        public void AnIdleActorTicksWithoutAllocating()
        {
            system.Tick(1f / 60f);

            Assert.That(() => { system.Tick(1f / 60f); }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void AskingWhetherAnAbilityCanStartAllocatesNothing()
        {
            AbilityContext context = AbilityContext.FromTarget(owner, null);
            system.EvaluateActivation(ability, context);

            Assert.That(
                () => { system.EvaluateActivation(ability, context); },
                Is.Not.AllocatingGCMemory(),
                "an AI scores every granted ability every frame with this call");
        }

        [Test]
        public void TheQuestionsAHudAsksAllocateNothing()
        {
            system.SetLooseTag(Rested, true);
            system.HasTag(Rested);
            system.GetCooldownRemaining(ability);
            system.GetCharges(ability);
            attributes.GetCurrent(Health);

            Assert.That(
                () =>
                {
                    system.HasTag(Rested);
                    system.IsOnCooldown(ability);
                    system.GetCooldownRemaining(ability);
                    system.GetCharges(ability);
                    attributes.GetCurrent(Health);
                    attributes.GetBase(Health);
                },
                Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void AnActiveEffectAgesWithoutAllocating()
        {
            GameplayEffectContainer container = GameplayEffectContainer.For(owner);
            GameplayEffectDefinition buff = ScriptableObject.CreateInstance<GameplayEffectDefinition>();
            buff.ConfigureForTests(
                GameplayEffectDurationPolicy.Duration,
                GameplayEffectTargeting.Owner,
                new[] { new GameplayEffectModifier(Health, AttributeOperation.Add, 10f) },
                1000f);
            container.Apply(new GameplayEffectSpec(buff, owner, owner));
            container.Tick(1f / 60f);

            Assert.That(() => { container.Tick(1f / 60f); }, Is.Not.AllocatingGCMemory());

            Object.DestroyImmediate(buff);
        }

        [Test]
        public void DiagnosticsCostOneBoolReadWhenTheyAreOff()
        {
            AbilityDiagnostics.Enabled = false;
            system.Record(AbilityEvent.Whiffed(ability));

            Assert.That(
                () => { system.Record(AbilityEvent.Whiffed(ability)); },
                Is.Not.AllocatingGCMemory(),
                "a record site that allocates while diagnostics are off is a shipped cost");
            Assert.IsFalse(system.HasHistory, "and it creates no ring either");
        }

        private static void SetField<TValue>(object target, string fieldName, TValue value)
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
    }
}
