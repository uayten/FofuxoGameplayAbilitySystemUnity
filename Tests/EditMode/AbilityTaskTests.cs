using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    /// <summary>
    /// The task lifetime contract and the wait tasks built on it: a task belongs
    /// to the activation that started it and never outlives it, whichever way
    /// that activation ends.
    /// </summary>
    public sealed class AbilityTaskTests
    {
        private const float Tick = 1f / 60f;
        private static readonly GameplayTag Chime = new("Event.Chime");

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

        // ------------------------------------------------------ task lifetime

        [Test]
        public void CompletingAnAbility_CancelsEveryTaskItStarted()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition ability = NewAbility("test.task.complete", 1);
            Grant(system, ability);

            Assert.IsTrue(system.TryActivate(ability, AbilityContext.FromTarget(owner, null)));
            var wait = system.RunTask(new WaitDelayTask(10f));
            Assert.IsNotNull(wait);
            Assert.IsTrue(wait.IsRunning);

            // The step is 10 frames long, so a generous tick completes it.
            system.Tick(1f);

            Assert.IsFalse(system.IsActive, "the ability completed");
            Assert.AreEqual(AbilityTaskState.Cancelled, wait.State);
        }

        [Test]
        public void CancellingAnAbility_CancelsEveryTaskItStarted()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition ability = NewAbility("test.task.cancel", 1);
            Grant(system, ability);

            Assert.IsTrue(system.TryActivate(ability, AbilityContext.FromTarget(owner, null)));
            var wait = system.RunTask(new WaitDelayTask(10f));
            system.ForceCancelActiveAbility(CommonGameplayTags.CancelManual);

            Assert.AreEqual(AbilityTaskState.Cancelled, wait.State);
            Assert.AreEqual(0, system.ActiveAbilityCount);
        }

        [Test]
        public void ACancelledTask_NeverTicksAgain()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition ability = NewAbility("test.task.dead", 1);
            Grant(system, ability);

            Assert.IsTrue(system.TryActivate(ability, AbilityContext.FromTarget(owner, null)));
            var wait = system.RunTask(new WaitDelayTask(1f));
            system.ForceCancelActiveAbility(CommonGameplayTags.CancelManual);

            float elapsedAtCancel = wait.ElapsedTime;
            for (int i = 0; i < 120; i++)
            {
                system.Tick(Tick);
            }

            Assert.AreEqual(elapsedAtCancel, wait.ElapsedTime, 0.0001f);
            Assert.AreEqual(AbilityTaskState.Cancelled, wait.State);
        }

        [Test]
        public void DisablingTheSystem_ReleasesActorTasksToo()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            var wait = system.RunActorTask(new WaitDelayTask(10f));
            Assert.IsTrue(wait.IsRunning);

            // A task with no owning activation is released by the component
            // teardown instead, which is the price of letting one exist at all.
            // Edit Mode never dispatches the lifecycle messages, so the test
            // sends the one the runtime would.
            InvokeMessage(system, "OnDisable");

            Assert.AreEqual(AbilityTaskState.Cancelled, wait.State);
        }

        [Test]
        public void RunTask_WithNothingRunning_StartsNothing()
        {
            AbilitySystem system = NewSystem(out GameObject owner);

            Assert.IsNull(system.RunTask(new WaitDelayTask(1f)));
        }

        [Test]
        public void AdvancingAStep_KeepsTheActivationTasks()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition combo = NewAbility("test.task.combo", 2);
            combo.SetStepAdvancementForTests(AbilityStepAdvancement.Automatic);
            Grant(system, combo);

            Assert.IsTrue(system.TryActivate(combo, AbilityContext.FromTarget(owner, null)));
            var wait = system.RunTask(new WaitDelayTask(10f));
            system.Tick(0.5f);

            Assert.AreEqual(1, system.ActiveStepIndex, "the combo chained");
            Assert.IsTrue(
                wait.IsRunning,
                "a task belongs to the activation, not to one of its steps");
        }

        // ---------------------------------------------------------- WaitDelay

        [Test]
        public void WaitDelay_SucceedsOnceItsTimeHasBeenTicked()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition ability = NewAbility("test.task.delay", 1);
            LongStep(ability);
            Grant(system, ability);

            Assert.IsTrue(system.TryActivate(ability, AbilityContext.FromTarget(owner, null)));
            var wait = system.RunTask(new WaitDelayTask(0.25f));

            for (int i = 0; i < 14; i++)
            {
                system.Tick(Tick);
            }

            Assert.IsTrue(wait.IsRunning, "0.233s of a 0.25s wait");
            system.Tick(Tick);
            system.Tick(Tick);
            Assert.AreEqual(AbilityTaskState.Succeeded, wait.State);
        }

        [Test]
        public void WaitDelay_OfZero_NeverEntersTheScope()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition ability = NewAbility("test.task.instant", 1);
            Grant(system, ability);

            Assert.IsTrue(system.TryActivate(ability, AbilityContext.FromTarget(owner, null)));
            var wait = system.RunTask(new WaitDelayTask(0f));

            Assert.AreEqual(AbilityTaskState.Succeeded, wait.State);
            Assert.AreEqual(0, system.ActiveInstances[0].Tasks.Count);
        }

        // ------------------------------------------------- WaitGameplayEvent

        [Test]
        public void WaitGameplayEvent_SucceedsOnItsTag()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition ability = NewAbility("test.task.event", 1);
            LongStep(ability);
            Grant(system, ability);

            Assert.IsTrue(system.TryActivate(ability, AbilityContext.FromTarget(owner, null)));
            var wait = system.RunTask(new WaitGameplayEventTask(Chime));

            system.TryHandleGameplayEvent(
                new GameplayTag("Event.Other"),
                AbilityContext.FromTarget(owner, null),
                out AbilityDefinition _);
            Assert.IsTrue(wait.IsRunning, "another tag is not this one");

            system.TryHandleGameplayEvent(
                Chime, AbilityContext.FromTarget(owner, null), out AbilityDefinition _);
            Assert.AreEqual(AbilityTaskState.Succeeded, wait.State);
        }

        [Test]
        public void WaitGameplayEvent_WithNoTag_FailsImmediately()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition ability = NewAbility("test.task.event.empty", 1);
            Grant(system, ability);

            Assert.IsTrue(system.TryActivate(ability, AbilityContext.FromTarget(owner, null)));
            var wait = system.RunTask(new WaitGameplayEventTask(default));

            Assert.AreEqual(AbilityTaskState.Failed, wait.State);
        }

        // ------------------------------------------------------- WaitInput

        [Test]
        public void WaitInputPress_SucceedsOnThePressEdgeOnly()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition ability = NewAbility("test.task.press", 1);
            LongStep(ability);
            Grant(system, ability);

            Assert.IsTrue(system.TryActivate(ability, AbilityContext.FromTarget(owner, null)));
            var wait = system.RunTask(WaitInputTask.Press());

            system.NotifyAbilityInput(AbilityInputEdge.Released);
            Assert.IsTrue(wait.IsRunning, "a release is not a press");

            system.NotifyAbilityInput(AbilityInputEdge.Pressed);
            Assert.AreEqual(AbilityTaskState.Succeeded, wait.State);
        }

        [Test]
        public void WaitInputRelease_IgnoresAnotherBinding()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition ability = NewAbility("test.task.release", 1);
            TimelineAbilityDefinition other = NewAbility("test.task.release.other", 1);
            LongStep(ability);
            Grant(system, ability, other);

            Assert.IsTrue(system.TryActivate(ability, AbilityContext.FromTarget(owner, null)));
            var wait = system.RunTask(WaitInputTask.Release(ability));

            system.NotifyAbilityInput(AbilityInputEdge.Released, other);
            Assert.IsTrue(wait.IsRunning, "another binding released, not this one");

            system.NotifyAbilityInput(AbilityInputEdge.Released, ability);
            Assert.AreEqual(AbilityTaskState.Succeeded, wait.State);
        }

        // ------------------------------------------------- WaitAnimationEvent

        [Test]
        public void WaitAnimationEvent_SucceedsOnItsName()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition ability = NewAbility("test.task.anim", 1);
            LongStep(ability);
            Grant(system, ability);

            Assert.IsTrue(system.TryActivate(ability, AbilityContext.FromTarget(owner, null)));
            var wait = system.RunTask(new WaitAnimationEventTask("Release"));

            system.NotifyAnimationEvent("Footstep");
            Assert.IsTrue(wait.IsRunning);

            system.NotifyAnimationEvent("Release");
            Assert.AreEqual(AbilityTaskState.Succeeded, wait.State);
        }

        [Test]
        public void AnimationEventBridge_ForwardsOnlyToTasks()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition ability = NewAbility("test.task.bridge", 1);
            LongStep(ability);
            Grant(system, ability);
            var bridge = owner.AddComponent<AbilityAnimationEventBridge>();

            var firedCues = new List<GameplayTag>();
            system.GameplayCueTriggered += cue => firedCues.Add(cue.Cue);

            Assert.IsTrue(system.TryActivate(ability, AbilityContext.FromTarget(owner, null)));
            var wait = system.RunTask(new WaitAnimationEventTask("Release"));

            bridge.EmitAbilityAnimationEvent("Release");

            Assert.AreEqual(AbilityTaskState.Succeeded, wait.State);
            Assert.AreEqual(
                0, firedCues.Count, "an ability animation event is not a cosmetic cue");
        }

        // ------------------------------------------------------ AcquireTargets

        [Test]
        public void AcquireTargets_FindsADamageableAndPointsTheActivationAtIt()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            GameObject target = NewObject("TaskTarget");
            target.transform.position = Vector3.forward * 3f;
            target.AddComponent<SphereCollider>();
            target.AddComponent<TaskStubReceiver>();
            TimelineAbilityDefinition ability = NewAbility("test.task.acquire", 1);
            LongStep(ability);
            Grant(system, ability);
            Physics.SyncTransforms();

            Assert.IsTrue(system.TryActivate(
                ability, AbilityContext.FromDirection(owner, null, Vector3.forward)));
            Assert.IsNull(system.ActiveContext?.Target);

            var acquire = system.RunTask(new AcquireTargetsTask(
                HitShape.Cone(1 << target.layer, 6f, 90f, 6f),
                default,
                AbilityTargetAcquireMode.Once,
                true));

            Assert.AreEqual(AbilityTaskState.Succeeded, acquire.State);
            Assert.AreEqual(target, system.ActiveContext?.Target);
            Assert.AreEqual(target, system.ActiveTargetData.PrimaryActor);
        }

        [Test]
        public void AcquireTargets_UntilFound_KeepsLookingAndDoesNotFailOnAnEmptyScene()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition ability = NewAbility("test.task.acquire.retry", 1);
            LongStep(ability);
            Grant(system, ability);

            Assert.IsTrue(system.TryActivate(
                ability, AbilityContext.FromDirection(owner, null, Vector3.forward)));
            var acquire = system.RunTask(new AcquireTargetsTask(
                // Layer 31 holds nothing in the test scene.
                HitShape.Cone(1 << 31, 6f, 90f, 6f),
                default,
                AbilityTargetAcquireMode.UntilFound));

            system.Tick(Tick);
            Assert.IsTrue(acquire.IsRunning);

            var onceOnly = system.RunTask(new AcquireTargetsTask(
                HitShape.Cone(1 << 31, 6f, 90f, 6f)));
            Assert.AreEqual(
                AbilityTaskState.Failed, onceOnly.State, "a single query that found nothing");
        }

        // ------------------------------------------------------------ helpers

        private GameObject NewObject(string name)
        {
            var created = new GameObject(name);
            owned.Add(created);
            return created;
        }

        private AbilitySystem NewSystem(out GameObject owner)
        {
            owner = NewObject("TaskAbilitySystemOwner");
            return owner.AddComponent<AbilitySystem>();
        }

        private TimelineAbilityDefinition NewAbility(string abilityId, int stepCount)
        {
            TimelineAbilityDefinition ability = ScriptableObject.CreateInstance<TimelineAbilityDefinition>();
            owned.Add(ability);
            ability.SetAbilityIdForTests(abilityId);
            SetField(ability, "requiresTarget", false);

            var steps = new AbilityStep[stepCount];
            for (int i = 0; i < stepCount; i++)
            {
                steps[i] = new AbilityStep();
                steps[i].ConfigureForTests(1, 2, 10, 60f);
            }

            ability.SetStepsForTests(steps);
            return ability;
        }

        /// <summary>A step long enough that no test tick ends the activation.</summary>
        private static void LongStep(TimelineAbilityDefinition ability)
        {
            ability.FirstStepForTests.ConfigureForTests(60, 120, 600, 60f);
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

        private static void InvokeMessage(object target, string methodName)
        {
            MethodInfo method = target.GetType().GetMethod(
                methodName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, methodName);
            method.Invoke(target, null);
        }

        /// <summary>Makes a collider an actor; acquisition accepts any living actor on its layers.</summary>
        private sealed class TaskStubReceiver : AttributeSet
        {
        }
    }
}
