using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    /// <summary>
    /// The movement tasks and the rules that decide which of them owns the body:
    /// deterministic travel under any frame pacing, no tick after a cancellation,
    /// and the priority and conflict policies that replaced the single
    /// oldest-activation-wins displacement.
    /// </summary>
    public sealed class AbilityMovementTaskTests
    {
        private const float Tick = 1f / 60f;
        private const float Tolerance = 0.001f;

        private readonly List<Object> owned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in owned)
            {
                if (o != null)
                {
                    Object.DestroyImmediate(o);
                }
            }

            owned.Clear();
        }

        // ---------------------------------------------------- determinism

        [Test]
        public void MoveByDistance_TravelsItsFullDistance_WhateverTheFramePacing()
        {
            // Same 6 m over 0.2 s, cut three ways. Every pacing covers at least
            // the duration, so the only thing under test is whether the total
            // depends on how the time arrived.
            var steady = new float[15];
            for (int i = 0; i < steady.Length; i++)
            {
                steady[i] = Tick;
            }

            float[] hitching = { 0.004f, 0.09f, 0.001f, 0.02f, 0.031f, 0.12f };
            float[] oneShot = { 1f };

            Assert.AreEqual(6f, TravelDistance(steady), Tolerance);
            Assert.AreEqual(6f, TravelDistance(hitching), Tolerance);
            Assert.AreEqual(6f, TravelDistance(oneShot), Tolerance);
        }

        [Test]
        public void MoveByDistance_NeverOvershoots_WhenATickIsLongerThanTheWindow()
        {
            var task = NewTravel(Vector3.right, 6f, 0.15f);
            Assert.IsTrue(task.TickMovement(10f, out Vector3 delta));

            Assert.AreEqual(6f, delta.magnitude, Tolerance);
            Assert.AreEqual(AbilityTaskState.Succeeded, task.State);
            Assert.IsFalse(task.TickMovement(Tick, out Vector3 _));
        }

        [Test]
        public void MoveByDistance_ZeroDeltaTime_SpendsNothing()
        {
            var task = NewTravel(Vector3.right, 5f, 0.5f);

            Assert.IsFalse(task.TickMovement(0f, out Vector3 delta));
            Assert.AreEqual(Vector3.zero, delta);
            Assert.IsTrue(task.IsRunning);
            Assert.AreEqual(5f, task.RemainingDistance, Tolerance);
        }

        [Test]
        public void MoveByDistance_IsPlanar()
        {
            var task = NewTravel(new Vector3(0f, 10f, 4f), 4f, 0.5f);
            Assert.IsTrue(task.TickMovement(1f, out Vector3 delta));

            Assert.AreEqual(0f, delta.y, Tolerance, "step travel never climbs");
            Assert.AreEqual(4f, delta.magnitude, Tolerance);
        }

        // ----------------------------------------------------- cancellation

        [Test]
        public void CancellingAnAbility_ProducesNoFurtherMovementTick()
        {
            AbilitySystem system = NewSystem(out GameObject owner, out RecordingMotor motor);
            TimelineAbilityDefinition dash = NewDash("test.move.cancel", 30f, 1, 60);
            Grant(system, dash);

            Assert.IsTrue(system.TryActivate(
                dash, AbilityContext.FromDirection(owner, null, Vector3.forward)));
            system.Tick(Tick);
            system.Tick(Tick);

            int movesBeforeCancel = motor.MoveCount;
            Vector3 travelBeforeCancel = motor.TotalDelta;
            Assert.Greater(movesBeforeCancel, 0, "the dash was moving");

            system.ForceCancelActiveAbility(CommonGameplayTags.CancelManual);
            for (int i = 0; i < 60; i++)
            {
                system.Tick(Tick);
            }

            Assert.AreEqual(movesBeforeCancel, motor.MoveCount, "not one extra move");
            Assert.AreEqual(travelBeforeCancel, motor.TotalDelta);
        }

        [Test]
        public void CancellingAKnockback_ProducesNoFurtherMovementTick()
        {
            AbilitySystem system = NewSystem(out GameObject owner, out RecordingMotor motor);
            system.ApplyKnockback(Vector3.back * 8f, 0.4f);

            system.TickFixed(0.02f);
            Assert.Greater(motor.MoveCount, 0);
            int movesBeforeCancel = motor.MoveCount;

            system.CancelKnockback();
            for (int i = 0; i < 30; i++)
            {
                system.TickFixed(0.02f);
            }

            Assert.AreEqual(movesBeforeCancel, motor.MoveCount);
            Assert.IsFalse(system.IsKnockbackActive);
        }

        // ------------------------------------------------------- knockback

        [Test]
        public void ApplyKnockback_TravelsVelocityTimesDuration_OnTheFixedStep()
        {
            AbilitySystem system = NewSystem(out GameObject owner, out RecordingMotor motor);

            ApplyKnockbackTask task = system.ApplyKnockback(new Vector3(0f, 2f, -6f), 0.5f, owner);
            Assert.IsNotNull(task);

            // The frame pass must not move a task that asked for the fixed step.
            system.Tick(Tick);
            Assert.AreEqual(0, motor.MoveCount, "knockback does not run on the frame pass");

            for (int i = 0; i < 40; i++)
            {
                system.TickFixed(0.02f);
            }

            Assert.AreEqual(AbilityTaskState.Succeeded, task.State);
            Assert.AreEqual(
                new Vector3(0f, 2f, -6f).magnitude * 0.5f,
                motor.TotalDelta.magnitude,
                Tolerance);
            Assert.Greater(motor.TotalDelta.y, 0f, "a launch keeps its vertical part");
        }

        [Test]
        public void ApplyKnockback_ReplacesThePreviousPushInsteadOfStacking()
        {
            AbilitySystem system = NewSystem(out GameObject owner, out RecordingMotor motor);
            ApplyKnockbackTask first = system.ApplyKnockback(Vector3.forward * 4f, 1f);
            ApplyKnockbackTask second = system.ApplyKnockback(Vector3.right * 4f, 1f);

            Assert.AreEqual(AbilityTaskState.Cancelled, first.State);
            Assert.IsTrue(second.IsRunning);

            system.TickFixed(0.02f);
            Assert.AreEqual(1, motor.MoveCount, "one push moved the body, not two");
        }

        [Test]
        public void ApplyKnockback_SurvivesTheCancellationOfAnUnrelatedAbility()
        {
            AbilitySystem system = NewSystem(out GameObject owner, out RecordingMotor _);
            TimelineAbilityDefinition combo = NewDash("test.move.unrelated", 0f, 1, 2);
            Grant(system, combo);

            Assert.IsTrue(system.TryActivate(combo, AbilityContext.FromTarget(owner, null)));
            ApplyKnockbackTask knockback = system.ApplyKnockback(Vector3.back * 6f, 0.5f);

            // The hit that caused the knockback also interrupts the combo. The
            // push is not part of that combo and must not die with it.
            system.ForceCancelActiveAbility(CommonGameplayTags.CancelManual);

            Assert.IsTrue(knockback.IsRunning);
            Assert.IsTrue(system.IsKnockbackActive);
        }

        [Test]
        public void ApplyKnockback_WithNoMovement_StartsNothing()
        {
            AbilitySystem system = NewSystem(out GameObject owner, out RecordingMotor _);

            Assert.IsNull(system.ApplyKnockback(Vector3.zero, 1f));
            Assert.IsNull(system.ApplyKnockback(Vector3.forward, 0f));
            Assert.IsFalse(system.IsKnockbackActive);
        }

        // -------------------------------------------- priority and conflict

        [Test]
        public void HigherPriorityMovement_TakesTheBodyAndTheLoserKeepsItsBudget()
        {
            AbilitySystem system = NewSystem(out GameObject owner, out RecordingMotor motor);
            TimelineAbilityDefinition dash = NewDash("test.move.priority", 30f, 1, 60);
            Grant(system, dash);

            Assert.IsTrue(system.TryActivate(
                dash, AbilityContext.FromDirection(owner, null, Vector3.forward)));
            AbilityMoveTask displacement = system.ActiveInstances[0].DisplacementTask;
            Assert.IsNotNull(displacement);

            // A knockback outranks the timeline, and shares its tick phase here
            // so the two actually contend.
            var knockback = new MoveByDistanceTask(Vector3.back, 5f, 1f)
            {
                Priority = AbilityMoveTask.KnockbackPriority,
            };
            system.RunTask(knockback);

            float budgetBefore = displacement.RemainingDistance;
            system.Tick(Tick);

            Assert.Less(knockback.RemainingDistance, 5f, "the higher priority moved");
            Assert.AreEqual(
                budgetBefore,
                displacement.RemainingDistance,
                Tolerance,
                "yielding spends nothing, so the travel is delayed and never lost");
            Assert.AreEqual(1, motor.MoveCount, "one task owned the body");
            Assert.Less(motor.TotalDelta.z, 0f, "the body went the knockback way");
        }

        [Test]
        public void BlendPolicy_AddsItsDeltaOnTopOfTheWinner()
        {
            AbilitySystem system = NewSystem(out GameObject owner, out RecordingMotor motor);
            TimelineAbilityDefinition dash = NewDash("test.move.blend", 30f, 1, 60);
            Grant(system, dash);

            Assert.IsTrue(system.TryActivate(
                dash, AbilityContext.FromDirection(owner, null, Vector3.forward)));
            var drift = new MoveByDistanceTask(Vector3.right, 6f, 1f)
            {
                ConflictPolicy = AbilityMovementConflictPolicy.Blend,
            };
            system.RunTask(drift);

            system.Tick(Tick);

            Assert.AreEqual(2, motor.MoveCount, "both tasks reached the motor");
            Assert.Greater(motor.TotalDelta.x, 0f);
            Assert.Greater(motor.TotalDelta.z, 0f);
        }

        [Test]
        public void AbortPolicy_EndsTheLoserRatherThanQueueingIt()
        {
            AbilitySystem system = NewSystem(out GameObject owner, out RecordingMotor _);
            TimelineAbilityDefinition dash = NewDash("test.move.abort", 30f, 1, 60);
            Grant(system, dash);

            Assert.IsTrue(system.TryActivate(
                dash, AbilityContext.FromDirection(owner, null, Vector3.forward)));
            var loser = new MoveByDistanceTask(Vector3.right, 6f, 1f)
            {
                Priority = AbilityMoveTask.DisplacementPriority - 1,
                ConflictPolicy = AbilityMovementConflictPolicy.Abort,
            };
            system.RunTask(loser);

            system.Tick(Tick);

            Assert.AreEqual(AbilityTaskState.Failed, loser.State);
        }

        // ------------------------------------------------- MoveTowardTarget

        [Test]
        public void MoveTowardTarget_StopsAtTheAuthoredGapBetweenTheTwoBodies()
        {
            GameObject owner = NewObject("MoveOwner");
            owner.AddComponent<SphereCollider>().radius = 1f;
            GameObject target = NewObject("MoveTarget");
            target.transform.position = Vector3.forward * 7f;
            target.AddComponent<SphereCollider>().radius = 0.5f;
            Physics.SyncTransforms();

            // A negative distance asks the task to measure the gap itself.
            var task = new MoveTowardTargetTask(target, -1f, 0.5f, 0.05f);
            var scope = new AbilityTaskScope();
            scope.Run(task, null, null, owner);

            // 7 apart, minus the target 0.5 surface, the owner 1.0 and the 0.05 gap.
            Assert.AreEqual(5.45f, task.RemainingDistance, 0.02f);
        }

        [Test]
        public void MoveTowardTarget_Tracking_FollowsATargetThatMoved()
        {
            GameObject owner = NewObject("TrackOwner");
            GameObject target = NewObject("TrackTarget");
            target.transform.position = Vector3.forward * 6f;

            var task = new MoveTowardTargetTask(
                target, 6f, 1f, 0f, AbilityMovementDirectionPolicy.Track);
            var scope = new AbilityTaskScope();
            scope.Run(task, null, null, owner);

            Assert.IsTrue(task.TickMovement(0.1f, out Vector3 first));
            Assert.Greater(first.z, 0f);

            target.transform.position = Vector3.right * 6f;
            Assert.IsTrue(task.TickMovement(0.1f, out Vector3 second));
            Assert.Greater(second.x, 0f, "tracking re-aimed at where the target went");
        }

        [Test]
        public void MoveTowardTarget_Snapshot_KeepsAimingWhereItStarted()
        {
            GameObject owner = NewObject("SnapshotOwner");
            GameObject target = NewObject("SnapshotTarget");
            target.transform.position = Vector3.forward * 6f;

            var task = new MoveTowardTargetTask(target, 6f, 1f);
            var scope = new AbilityTaskScope();
            scope.Run(task, null, null, owner);

            target.transform.position = Vector3.right * 6f;
            Assert.IsTrue(task.TickMovement(0.1f, out Vector3 delta));

            Assert.Greater(delta.z, 0f, "a snapshot commits to where it was aimed");
            Assert.AreEqual(0f, delta.x, Tolerance);
        }

        [Test]
        public void MoveTowardTarget_LosingTheTarget_EndsTheTravel()
        {
            GameObject owner = NewObject("LostOwner");
            GameObject target = NewObject("LostTarget");
            target.transform.position = Vector3.forward * 6f;

            var task = new MoveTowardTargetTask(
                target, 6f, 1f, 0f, AbilityMovementDirectionPolicy.Track);
            var scope = new AbilityTaskScope();
            scope.Run(task, null, null, owner);

            Object.DestroyImmediate(target);
            owned.Remove(target);

            Assert.IsFalse(task.TickMovement(0.1f, out Vector3 _));
            Assert.AreEqual(AbilityTaskState.Failed, task.State);
        }

        // ------------------------------------------------------------ helpers

        private static float TravelDistance(float[] deltas)
        {
            var task = NewTravel(Vector3.right, 6f, 0.2f);
            float travelled = 0f;
            foreach (float delta in deltas)
            {
                if (task.TickMovement(delta, out Vector3 step))
                {
                    travelled += step.magnitude;
                }
            }

            return travelled;
        }

        private static MoveByDistanceTask NewTravel(
            Vector3 direction, float distance, float duration)
        {
            var task = new MoveByDistanceTask(direction, distance, duration);
            new AbilityTaskScope().Run(task, null, null, null);
            return task;
        }

        private GameObject NewObject(string name)
        {
            var created = new GameObject(name);
            owned.Add(created);
            return created;
        }

        private AbilitySystem NewSystem(out GameObject owner, out RecordingMotor motor)
        {
            owner = NewObject("MovementAbilitySystemOwner");
            AbilitySystem system = owner.AddComponent<AbilitySystem>();
            motor = new RecordingMotor();
            system.Motor = motor;
            return system;
        }

        private TimelineAbilityDefinition NewDash(
            string abilityId, float distance, int startFrame, int endFrame)
        {
            TimelineAbilityDefinition ability = ScriptableObject.CreateInstance<TimelineAbilityDefinition>();
            owned.Add(ability);
            ability.SetAbilityIdForTests(abilityId);
            SetField(ability, "requiresTarget", false);
            ability.FirstStepForTests.ConfigureForTests(30, 60, 120, 60f);
            ability.FirstStepForTests.ConfigureDisplacementForTests(
                AbilityDisplacementDirection.Context, distance, startFrame, endFrame);
            return ability;
        }

        private void Grant(AbilitySystem system, params AbilityDefinition[] abilities)
        {
            AbilityLoadout loadout = ScriptableObject.CreateInstance<AbilityLoadout>();
            owned.Add(loadout);
            SetField(loadout, "abilities", abilities);
            SetField(system, "loadout", loadout);
        }

        private static void SetField<TValue>(object target, string fieldName, TValue value)
        {
            // Walks up the hierarchy: a private field of a base class is
            // invisible to a single GetField call, and an ability's own fields
            // now sit one level above the timeline type most fixtures use.
            for (System.Type type = target.GetType(); type != null; type = type.BaseType)
            {
                FieldInfo field = type.GetField(
                    fieldName,
                    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    field.SetValue(target, value);
                    return;
                }
            }

            Assert.Fail($"No field named {fieldName}.");
        }

        /// <summary>
        /// Stands in for a real motor so a test can count exactly how many times
        /// the body was moved, which is what "no extra tick" has to be measured
        /// against.
        /// </summary>
        private sealed class RecordingMotor : IAbilityMotor
        {
            public int MoveCount { get; private set; }
            public Vector3 TotalDelta { get; private set; }
            public AbilityMovementCollision LastCollision { get; private set; }

            public Vector3 Position => TotalDelta;

            public Vector3 Move(Vector3 delta, AbilityMovementCollision collision)
            {
                MoveCount++;
                TotalDelta += delta;
                LastCollision = collision;
                return delta;
            }
        }
    }
}
