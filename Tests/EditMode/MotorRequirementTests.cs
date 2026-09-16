using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    /// <summary>
    /// Whether an ability's travel can reach the world is a fact about the
    /// actor, not about the asset, so the asset cannot validate it and the
    /// system has to.
    ///
    /// Before this, an owner with no motor and no Rigidbody got a motor anyway:
    /// a wrapper around a null body that accepted every displacement and moved
    /// nothing. The ability activated, played its animation, held its tags,
    /// finished, and travelled zero metres — which reads as a broken animation
    /// rather than as a missing component.
    /// </summary>
    public sealed class MotorRequirementTests
    {
        /// <summary>A motor that exists and refuses to move, which is a valid
        /// answer: what matters here is that the seam is filled.</summary>
        private sealed class StubMotor : IAbilityMotor
        {
            public Vector3 Position => Vector3.zero;

            public Vector3 Move(Vector3 delta, AbilityMovementCollision collision) =>
                Vector3.zero;
        }

        private readonly List<Object> owned = new();

        [TearDown]
        public void TearDown()
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

        [Test]
        public void AnOwnerWithNeitherMotorNorRigidbodyHasNoMotor()
        {
            AbilitySystem system = NewSystem(NewAbility("test.motor.none", moves: false));

            Assert.IsNull(system.Motor);
            Assert.IsFalse(system.HasMotor);
        }

        [Test]
        public void ARigidbodyIsStillEnoughOfAMotor()
        {
            AbilitySystem system = NewSystem(NewAbility("test.motor.body", moves: false));
            system.gameObject.AddComponent<Rigidbody>();

            Assert.IsTrue(system.HasMotor, "a project that never opted into the seam still moves");
        }

        [Test]
        public void AnAssignedMotorIsUsedWhateverTheActorCarries()
        {
            AbilitySystem system = NewSystem(NewAbility("test.motor.assigned", moves: false));
            system.Motor = new StubMotor();

            Assert.IsTrue(system.HasMotor);
        }

        [Test]
        public void ADisplacingAbilityIsRefusedOnAnActorThatCannotMove()
        {
            TimelineAbilityDefinition ability = NewAbility("test.motor.displace", moves: true);
            AbilitySystem system = NewSystem(ability);

            Assert.IsFalse(
                system.TryActivate(
                    ability,
                    AbilityContext.FromTarget(system.gameObject, null),
                    out AbilityActivationResult result));

            Assert.AreEqual(AbilityActivationRejection.MissingMotor, result.Rejection);
            Assert.AreEqual(0, system.ActiveAbilityCount);
        }

        [Test]
        public void TheSameAbilityActivatesOnceTheActorCanMove()
        {
            TimelineAbilityDefinition ability = NewAbility("test.motor.displace.ok", moves: true);
            AbilitySystem system = NewSystem(ability);
            system.gameObject.AddComponent<Rigidbody>();

            Assert.IsTrue(system.TryActivate(
                ability, AbilityContext.FromTarget(system.gameObject, null)));
        }

        /// <summary>
        /// An ability that never moves its owner does not need one, which is
        /// most of them: a block, a cast, a swing that stays put.
        /// </summary>
        [Test]
        public void AStationaryAbilityDoesNotNeedAMotor()
        {
            TimelineAbilityDefinition ability = NewAbility("test.motor.stationary", moves: false);
            AbilitySystem system = NewSystem(ability);

            Assert.IsFalse(ability.RequiresMotor);
            Assert.IsTrue(system.TryActivate(
                ability, AbilityContext.FromTarget(system.gameObject, null)));
        }

        [Test]
        public void AnApproachingTargetAssistAlsoRequiresAMotor()
        {
            TargetAssistDefinition assist =
                Own(ScriptableObject.CreateInstance<TargetAssistDefinition>());
            assist.name = "GTA_Test_Approach";
            SetField(assist, "approachTarget", true);

            TimelineAbilityDefinition ability = NewAbility("test.motor.assist", moves: false);
            ability.SetTargetAssistForTests(assist);

            Assert.IsTrue(ability.RequiresMotor, "the approach is travel like any other");
        }

        [Test]
        public void AnAssistThatOnlyRotatesNeedsNoMotor()
        {
            TargetAssistDefinition assist =
                Own(ScriptableObject.CreateInstance<TargetAssistDefinition>());
            assist.name = "GTA_Test_Snap";
            SetField(assist, "approachTarget", false);

            TimelineAbilityDefinition ability = NewAbility("test.motor.assist.snap", moves: false);
            ability.SetTargetAssistForTests(assist);

            Assert.IsFalse(ability.RequiresMotor);
        }

        /// <summary>
        /// Evaluating must not start anything, and the motor check is on the
        /// same path as every other rule.
        /// </summary>
        [Test]
        public void EvaluatingTheRefusalStartsNothing()
        {
            TimelineAbilityDefinition ability = NewAbility("test.motor.sideeffect", moves: true);
            AbilitySystem system = NewSystem(ability);

            AbilityActivationResult result = system.EvaluateActivation(
                ability, AbilityContext.FromTarget(system.gameObject, null));

            Assert.AreEqual(AbilityActivationRejection.MissingMotor, result.Rejection);
            Assert.AreEqual(0, system.ActiveAbilityCount);
            Assert.IsFalse(system.IsActive);
        }

        // ------------------------------------------------------------ helpers

        private T Own<T>(T item) where T : Object
        {
            owned.Add(item);
            return item;
        }

        private TimelineAbilityDefinition NewAbility(string abilityId, bool moves)
        {
            TimelineAbilityDefinition ability = Own(ScriptableObject.CreateInstance<TimelineAbilityDefinition>());
            ability.SetAbilityIdForTests(abilityId);
            SetField(ability, "requiresTarget", false);
            AbilityStep step = new();
            step.ConfigureForTests(1, 2, 30, 60f);
            if (moves)
            {
                step.ConfigureDisplacementForTests(
                    AbilityDisplacementDirection.OwnerForward, 3f, 1, 10);
            }

            ability.SetStepsForTests(step);
            return ability;
        }

        private AbilitySystem NewSystem(AbilityDefinition ability)
        {
            GameObject owner = Own(new GameObject("MotorRequirementOwner"));
            AbilityLoadout loadout = Own(ScriptableObject.CreateInstance<AbilityLoadout>());
            SetField(loadout, "abilities", new[] { ability });
            AbilitySystem system = owner.AddComponent<AbilitySystem>();
            SetField(system, "loadout", loadout);
            return system;
        }

        private static void SetField<TValue>(object target, string name, TValue value)
        {
            for (System.Type type = target.GetType(); type != null; type = type.BaseType)
            {
                FieldInfo field = type.GetField(
                    name,
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                {
                    field.SetValue(target, value);
                    return;
                }
            }

            Assert.Fail($"No field named {name}.");
        }
    }
}
