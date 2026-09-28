using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Assemblies;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    /// <summary>
    /// The engine seam. Each adapter is exercised on its own, without an ability
    /// anywhere near it, and the assembly they live in is checked to depend on
    /// nothing from the game project — that boundary is the reason the seam
    /// exists at all.
    /// </summary>
    public sealed class AbilityMotorTests
    {
        private const float Tolerance = 0.001f;

        private readonly List<UnityEngine.Object> owned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (UnityEngine.Object o in owned)
            {
                if (o != null)
                {
                    UnityEngine.Object.DestroyImmediate(o);
                }
            }

            owned.Clear();
        }

        // --------------------------------------------------- the boundary

        [Test]
        public void TheMotorsAssembly_ReferencesNoGameAssembly()
        {
            Assembly motors = CurrentAssemblies.GetLoadedAssemblies().FirstOrDefault(
                candidate => candidate.GetName().Name ==
                    "Uayten.FofuxoGameplayAbilitySystem.Motors");
            Assert.IsNotNull(motors, "the Motors assembly is compiled");

            string[] referenced = motors
                .GetReferencedAssemblies()
                .Select(reference => reference.Name)
                .ToArray();

            // Assembly-CSharp is where Assets/Scripts compiles to. A package that
            // reached into it would stop being portable, and a consumer motor
            // would stop being the consumer business.
            CollectionAssert.DoesNotContain(referenced, "Assembly-CSharp");
            CollectionAssert.DoesNotContain(referenced, "Assembly-CSharp-Editor");
            CollectionAssert.Contains(referenced, "Uayten.FofuxoGameplayAbilitySystem");
        }

        [Test]
        public void EveryShippedMotor_IsAnIAbilityMotor()
        {
            Assert.IsTrue(typeof(IAbilityMotor).IsAssignableFrom(typeof(AbilityRigidbodyMotor)));
            Assert.IsTrue(
                typeof(IAbilityMotor).IsAssignableFrom(typeof(AbilityCharacterControllerMotor)));
            Assert.IsTrue(typeof(IAbilityMotor).IsAssignableFrom(typeof(AbilityDelegateMotor)));
        }

        // ------------------------------------------------------- Rigidbody

        [Test]
        public void RigidbodyMotor_AccumulatesWithinOnePhysicsStep()
        {
            GameObject actor = NewObject("RigidbodyMotorActor");
            Rigidbody body = actor.AddComponent<Rigidbody>();
            body.isKinematic = true;
            var motor = actor.AddComponent<AbilityRigidbodyMotor>();

            // Three frame deltas between two physics steps: a motor that flushed
            // on the spot would keep only the last, and under-travel.
            motor.Move(Vector3.right * 0.1f, AbilityMovementCollision.Unswept);
            motor.Move(Vector3.right * 0.1f, AbilityMovementCollision.Unswept);
            motor.Move(Vector3.right * 0.1f, AbilityMovementCollision.Unswept);

            Assert.AreEqual(
                new Vector3(0.3f, 0f, 0f), PendingDelta(motor), "nothing was dropped");

            InvokeFixedUpdate(motor);
            Assert.AreEqual(Vector3.zero, PendingDelta(motor), "the step flushed it");
        }

        [Test]
        public void RigidbodyMotor_ReportsWhatItAccepted()
        {
            GameObject actor = NewObject("RigidbodyMotorReport");
            Rigidbody body = actor.AddComponent<Rigidbody>();
            body.isKinematic = true;
            var motor = actor.AddComponent<AbilityRigidbodyMotor>();

            Assert.AreEqual(
                Vector3.zero, motor.Move(Vector3.zero, AbilityMovementCollision.Unswept));
            Assert.AreEqual(
                Vector3.forward,
                motor.Move(Vector3.forward, AbilityMovementCollision.Unswept));
        }

        // --------------------------------------------- character controller

        [Test]
        public void CharacterControllerMotor_UnsweptTravelBypassesTheController()
        {
            GameObject actor = NewObject("ControllerMotorActor");
            actor.AddComponent<CharacterController>();
            var motor = actor.AddComponent<AbilityCharacterControllerMotor>();

            Vector3 applied = motor.Move(
                new Vector3(0f, 0f, 2f), AbilityMovementCollision.Unswept);

            Assert.AreEqual(new Vector3(0f, 0f, 2f), applied);
            Assert.AreEqual(2f, actor.transform.position.z, Tolerance);
        }

        [Test]
        public void CharacterControllerMotor_MotorAuthorityGoesThroughTheController()
        {
            GameObject actor = NewObject("ControllerAuthorityActor");
            actor.AddComponent<CharacterController>();
            var motor = actor.AddComponent<AbilityCharacterControllerMotor>();

            // No blockers in the test scene, so the controller resolves the full
            // delta and swept travel agrees with unswept travel in open space.
            Vector3 applied = motor.Move(
                new Vector3(0f, 0f, 1f), AbilityMovementCollision.MotorAuthority);

            Assert.AreEqual(1f, applied.z, 0.02f);
        }

        // --------------------------------------------------------- delegate

        [Test]
        public void DelegateMotor_HandsTheDeltaToTheConsumer()
        {
            GameObject actor = NewObject("DelegateMotorActor");
            var motor = actor.AddComponent<AbilityDelegateMotor>();
            Vector3 received = Vector3.zero;
            AbilityMovementCollision receivedMode = AbilityMovementCollision.Unswept;

            motor.MoveHandler = (delta, collision) =>
            {
                received = delta;
                receivedMode = collision;
                // A consumer motor is free to accept only part of it.
                return delta * 0.5f;
            };

            Vector3 applied = motor.Move(
                Vector3.forward * 2f, AbilityMovementCollision.MotorAuthority);

            Assert.AreEqual(Vector3.forward * 2f, received);
            Assert.AreEqual(AbilityMovementCollision.MotorAuthority, receivedMode);
            Assert.AreEqual(Vector3.forward, applied);
        }

        [Test]
        public void DelegateMotor_WithNoHandler_RefusesQuietly()
        {
            GameObject actor = NewObject("DelegateMotorEmpty");
            var motor = actor.AddComponent<AbilityDelegateMotor>();

            Assert.AreEqual(
                Vector3.zero,
                motor.Move(Vector3.forward, AbilityMovementCollision.MotorAuthority));
        }

        // ------------------------------------------------------- resolution

        [Test]
        public void TheSystem_PrefersAMotorComponentOverTheRigidbodyFallback()
        {
            GameObject actor = NewObject("MotorResolutionActor");
            actor.AddComponent<Rigidbody>().isKinematic = true;
            var motor = actor.AddComponent<AbilityDelegateMotor>();
            AbilitySystem system = actor.AddComponent<AbilitySystem>();

            Assert.AreSame(motor, system.Motor);
        }

        [Test]
        public void TheSystem_WithoutAMotorComponent_StillMoves()
        {
            GameObject actor = NewObject("MotorFallbackActor");
            actor.AddComponent<Rigidbody>().isKinematic = true;
            AbilitySystem system = actor.AddComponent<AbilitySystem>();

            Assert.IsNotNull(
                system.Motor,
                "a project that never opted into the seam keeps its displacement");
        }

        // ------------------------------------------------------------ helpers

        private GameObject NewObject(string name)
        {
            var created = new GameObject(name);
            owned.Add(created);
            return created;
        }

        private static Vector3 PendingDelta(AbilityRigidbodyMotor motor)
        {
            FieldInfo field = typeof(AbilityRigidbodyMotor).GetField(
                "pendingDelta", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field);
            return (Vector3)field.GetValue(motor);
        }

        private static void InvokeFixedUpdate(AbilityRigidbodyMotor motor)
        {
            MethodInfo method = typeof(AbilityRigidbodyMotor).GetMethod(
                "FixedUpdate", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method);
            method.Invoke(motor, null);
        }
    }
}
