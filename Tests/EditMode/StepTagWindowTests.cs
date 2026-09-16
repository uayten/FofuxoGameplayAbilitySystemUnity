using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    /// <summary>
    /// Tag windows are how an ability's behaviour is authored: i-frames and
    /// movement locks that end on an exact frame, well before the animation
    /// does. Two guarantees carry the weight — windows sharing frames switch on
    /// the same tick, and no window survives the activation that opened it.
    /// </summary>
    public sealed class StepTagWindowTests
    {
        private const float Frame = 1f / 60f;
        private static readonly GameplayTag Immune = CommonGameplayTags.Invulnerable;
        private static readonly GameplayTag MovementLocked = CommonGameplayTags.MovementLocked;
        private static readonly GameplayTag Rolling = CommonGameplayTags.Rolling;

        private readonly List<Object> owned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (Object ownedObject in owned)
            {
                if (ownedObject != null)
                {
                    Object.DestroyImmediate(ownedObject);
                }
            }

            owned.Clear();
        }

        [Test]
        public void WindowIsClosedBeforeItsStartFrame_AndOpensOnIt()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition ability = NewAbility("test.window", recoveryEndFrame: 20);
            ability.FirstStepForTests.SetTagWindowsForTests(new AbilityStepTagWindow(Immune, 3, 8));
            Grant(system, ability);

            Assert.IsTrue(system.TryActivate(ability, AbilityContext.FromTarget(owner, null)));
            Assert.IsFalse(system.HasTag(Immune), "no window is open before the first tick");

            TickToFrame(system, 2);
            Assert.IsFalse(system.HasTag(Immune), "frame 2 is still before the window");

            TickToFrame(system, 3);
            Assert.IsTrue(system.HasTag(Immune), "the window opens on its start frame");
        }

        [Test]
        public void WindowClosesAfterItsEndFrame_WhileTheAbilityKeepsRunning()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition ability = NewAbility("test.window.end", recoveryEndFrame: 30);
            ability.FirstStepForTests.SetTagWindowsForTests(new AbilityStepTagWindow(Immune, 1, 10));
            Grant(system, ability);

            system.TryActivate(ability, AbilityContext.FromTarget(owner, null));

            TickToFrame(system, 10);
            Assert.IsTrue(system.HasTag(Immune), "still held on the last frame of the window");

            TickToFrame(system, 11);
            Assert.IsFalse(system.HasTag(Immune), "released on the frame after");
            Assert.IsTrue(system.IsActive, "the ability outlives its window");
        }

        [Test]
        public void WindowsSharingFrames_OpenAndCloseOnTheSameTick()
        {
            // The reason the feature exists: immunity and the movement lock have
            // to be one decision, not two timers that can drift apart.
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition ability = NewAbility("test.window.paired", recoveryEndFrame: 30);
            ability.FirstStepForTests.SetTagWindowsForTests(
                new AbilityStepTagWindow(Immune, 2, 12),
                new AbilityStepTagWindow(MovementLocked, 2, 12));
            Grant(system, ability);

            system.TryActivate(ability, AbilityContext.FromTarget(owner, null));

            TickToFrame(system, 1);
            Assert.IsFalse(system.HasTag(Immune));
            Assert.IsFalse(system.HasTag(MovementLocked));

            TickToFrame(system, 2);
            Assert.IsTrue(system.HasTag(Immune));
            Assert.IsTrue(system.HasTag(MovementLocked));

            TickToFrame(system, 12);
            Assert.IsTrue(system.HasTag(Immune));
            Assert.IsTrue(system.HasTag(MovementLocked));

            TickToFrame(system, 13);
            Assert.IsFalse(system.HasTag(Immune));
            Assert.IsFalse(system.HasTag(MovementLocked));
        }

        [Test]
        public void ZeroEndFrame_HoldsTheTagThroughTheEndOfTheStep()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition ability = NewAbility("test.window.open-ended", recoveryEndFrame: 12);
            ability.FirstStepForTests.SetTagWindowsForTests(new AbilityStepTagWindow(Immune, 4, 0));
            Grant(system, ability);

            system.TryActivate(ability, AbilityContext.FromTarget(owner, null));

            TickToFrame(system, 4);
            Assert.IsTrue(system.HasTag(Immune));

            TickToFrame(system, 12);
            Assert.IsTrue(system.HasTag(Immune), "still held on the step's last frame");
        }

        [Test]
        public void CompletingTheAbility_ClosesEveryOpenWindow()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition ability = NewAbility("test.window.complete", recoveryEndFrame: 6);
            ability.FirstStepForTests.SetTagWindowsForTests(new AbilityStepTagWindow(Immune, 1, 0));
            Grant(system, ability);

            system.TryActivate(ability, AbilityContext.FromTarget(owner, null));
            TickToFrame(system, 3);
            Assert.IsTrue(system.HasTag(Immune));

            for (int i = 0; i < 10 && system.IsActive; i++)
            {
                system.Tick(Frame);
            }

            Assert.IsFalse(system.IsActive);
            Assert.IsFalse(system.HasTag(Immune), "a completed ability leaves no i-frames behind");
        }

        [Test]
        public void CancellingTheAbility_ClosesEveryOpenWindow()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition ability = NewAbility("test.window.cancel", recoveryEndFrame: 30);
            ability.FirstStepForTests.SetTagWindowsForTests(
                new AbilityStepTagWindow(Immune, 1, 0),
                new AbilityStepTagWindow(MovementLocked, 1, 0));
            Grant(system, ability);

            system.TryActivate(ability, AbilityContext.FromTarget(owner, null));
            TickToFrame(system, 5);
            Assert.IsTrue(system.HasTag(Immune));

            system.ForceCancelActiveAbility(new GameplayTag("Cancel.HitReaction"));

            Assert.IsFalse(system.HasTag(Immune), "a cancelled roll cannot stay immune");
            Assert.IsFalse(system.HasTag(MovementLocked));
        }

        [Test]
        public void AdvancingToTheNextStep_ClosesThePreviousStepWindows()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition combo = NewAbility("test.window.combo", recoveryEndFrame: 4);
            var second = new AbilityStep();
            second.ConfigureForTests(1, 2, 4, 60f);
            AbilityStep first = combo.FirstStepForTests;
            first.SetTagWindowsForTests(new AbilityStepTagWindow(Immune, 1, 0));
            combo.SetStepsForTests(first, second);
            Grant(system, combo);

            system.TryActivate(combo, AbilityContext.FromTarget(owner, null));
            TickToFrame(system, 2);
            Assert.IsTrue(system.HasTag(Immune));

            for (int i = 0; i < 6 && system.ActiveStepIndex == 0; i++)
            {
                system.Tick(Frame);
            }

            Assert.AreEqual(1, system.ActiveStepIndex, "the combo advanced");
            Assert.IsFalse(
                system.HasTag(Immune), "a window belongs to the step that authored it");
        }

        [Test]
        public void WindowTagAlsoGrantedByTheAbility_SurvivesTheWindowClosing()
        {
            // Counted, not a set: the ability-wide grant must outlive the window.
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition ability = NewAbility("test.window.counted", recoveryEndFrame: 30);
            SetField(ability, "grantedTags", new[] { Rolling });
            ability.FirstStepForTests.SetTagWindowsForTests(new AbilityStepTagWindow(Rolling, 1, 5));
            Grant(system, ability);

            system.TryActivate(ability, AbilityContext.FromTarget(owner, null));
            TickToFrame(system, 3);
            Assert.IsTrue(system.HasTag(Rolling));

            TickToFrame(system, 6);
            Assert.IsTrue(system.HasTag(Rolling), "the ability-wide grant is still held");
            Assert.IsFalse(
                system.IsStepTagWindowOpen(Rolling),
                "the window query ignores the ability-wide grant");

            system.ForceCancelActiveAbility(CommonGameplayTags.CancelManual);
            Assert.IsFalse(system.HasTag(Rolling));
        }

        [Test]
        public void OverlappingWindowsForTheSameTag_CloseOnlyAfterTheLastWindow()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition ability = NewAbility("test.window.overlap", recoveryEndFrame: 20);
            ability.FirstStepForTests.SetTagWindowsForTests(
                new AbilityStepTagWindow(Immune, 1, 5),
                new AbilityStepTagWindow(Immune, 3, 8));
            Grant(system, ability);

            var transitions = new List<bool>();
            system.StepTagWindowChanged += (tag, isOpen) =>
            {
                if (tag == Immune)
                {
                    transitions.Add(isOpen);
                }
            };

            system.TryActivate(ability, AbilityContext.FromTarget(owner, null));
            TickToFrame(system, 1);
            Assert.IsTrue(system.HasTag(Immune));
            CollectionAssert.AreEqual(new[] { true }, transitions);

            TickToFrame(system, 6);
            Assert.IsTrue(system.HasTag(Immune), "the second window still holds the tag");
            CollectionAssert.AreEqual(
                new[] { true }, transitions, "no false close edge was emitted");

            TickToFrame(system, 9);
            Assert.IsFalse(system.HasTag(Immune));
            CollectionAssert.AreEqual(new[] { true, false }, transitions);
        }

        [Test]
        public void AdjacentWindowsForTheSameTag_DoNotEmitAFalseSeam()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition ability = NewAbility("test.window.adjacent", recoveryEndFrame: 20);
            ability.FirstStepForTests.SetTagWindowsForTests(
                new AbilityStepTagWindow(Immune, 1, 3),
                new AbilityStepTagWindow(Immune, 4, 6));
            Grant(system, ability);

            var transitions = new List<bool>();
            system.StepTagWindowChanged += (tag, isOpen) =>
            {
                if (tag == Immune)
                {
                    transitions.Add(isOpen);
                }
            };

            system.TryActivate(ability, AbilityContext.FromTarget(owner, null));
            TickToFrame(system, 4);

            Assert.IsTrue(system.HasTag(Immune));
            CollectionAssert.AreEqual(new[] { true }, transitions);

            TickToFrame(system, 7);
            CollectionAssert.AreEqual(new[] { true, false }, transitions);
        }

        [Test]
        public void StepTagWindowChanged_FiresOncePerTransition()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition ability = NewAbility("test.window.event", recoveryEndFrame: 30);
            ability.FirstStepForTests.SetTagWindowsForTests(new AbilityStepTagWindow(Immune, 3, 6));
            Grant(system, ability);

            var transitions = new List<(GameplayTag Tag, bool Open)>();
            system.StepTagWindowChanged += (tag, open) => transitions.Add((tag, open));

            system.TryActivate(ability, AbilityContext.FromTarget(owner, null));
            TickToFrame(system, 10);

            Assert.AreEqual(2, transitions.Count, "one open and one close, no repeats");
            Assert.AreEqual((Immune, true), transitions[0]);
            Assert.AreEqual((Immune, false), transitions[1]);
        }

        [Test]
        public void WindowClosesBeforeCompletionEvent()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition ability = NewAbility("test.window.complete-order", recoveryEndFrame: 4);
            ability.FirstStepForTests.SetTagWindowsForTests(new AbilityStepTagWindow(Immune, 1, 0));
            Grant(system, ability);

            var events = new List<string>();
            system.StepTagWindowChanged += (_, isOpen) =>
                events.Add(isOpen ? "window-open" : "window-close");
            system.AbilityCompleted += _ => events.Add("ability-completed");

            system.TryActivate(ability, AbilityContext.FromTarget(owner, null));
            while (system.IsActive)
            {
                system.Tick(Frame);
            }

            CollectionAssert.AreEqual(
                new[] { "window-open", "window-close", "ability-completed" },
                events);
        }

        [Test]
        public void WindowClosesBeforeCancellationEvent()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition ability = NewAbility("test.window.cancel-order", recoveryEndFrame: 20);
            ability.FirstStepForTests.SetTagWindowsForTests(new AbilityStepTagWindow(Immune, 1, 0));
            Grant(system, ability);

            var events = new List<string>();
            system.StepTagWindowChanged += (_, isOpen) =>
                events.Add(isOpen ? "window-open" : "window-close");
            system.AbilityCancelled += (_, _) => events.Add("ability-cancelled");

            system.TryActivate(ability, AbilityContext.FromTarget(owner, null));
            TickToFrame(system, 2);
            system.ForceCancelActiveAbility(CommonGameplayTags.CancelManual);

            CollectionAssert.AreEqual(
                new[] { "window-open", "window-close", "ability-cancelled" },
                events);
        }

        [Test]
        public void DisablingTheSystem_ClosesOpenWindows()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition ability = NewAbility("test.window.disable", recoveryEndFrame: 20);
            ability.FirstStepForTests.SetTagWindowsForTests(new AbilityStepTagWindow(Immune, 1, 0));
            Grant(system, ability);

            system.TryActivate(ability, AbilityContext.FromTarget(owner, null));
            TickToFrame(system, 2);
            InvokeLifecycle(system, "OnDisable");

            Assert.IsFalse(system.IsActive);
            Assert.IsFalse(system.HasTag(Immune));
        }

        [Test]
        public void DestroyingTheSystem_EmitsOneCloseTransition()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition ability = NewAbility("test.window.destroy", recoveryEndFrame: 20);
            ability.FirstStepForTests.SetTagWindowsForTests(new AbilityStepTagWindow(Immune, 1, 0));
            Grant(system, ability);

            int closeCount = 0;
            system.StepTagWindowChanged += (_, isOpen) =>
            {
                if (!isOpen)
                {
                    closeCount++;
                }
            };

            system.TryActivate(ability, AbilityContext.FromTarget(owner, null));
            TickToFrame(system, 2);
            InvokeLifecycle(system, "OnDestroy");
            Object.DestroyImmediate(system);

            Assert.AreEqual(1, closeCount);
        }

        [Test]
        public void MovementLockedWindow_ReleasesMovementBeforeTheAbilityEnds()
        {
            // The roll shape: movement comes back at frame 10 while the clip
            // keeps playing to frame 30.
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition ability = NewAbility("test.window.movement", recoveryEndFrame: 30);
            SetField(ability, "lockMovementDuringAbility", false);
            ability.FirstStepForTests.SetTagWindowsForTests(
                new AbilityStepTagWindow(MovementLocked, 1, 10));
            Grant(system, ability);

            system.TryActivate(ability, AbilityContext.FromTarget(owner, null));

            TickToFrame(system, 10);
            Assert.IsTrue(system.IsMovementLocked, "still locked on the window's last frame");

            TickToFrame(system, 11);
            Assert.IsFalse(system.IsMovementLocked, "movement is released by the window");
            Assert.IsTrue(system.IsActive, "and the ability is still playing its animation");
        }

        [Test]
        public void EmptyTagWindow_FailsValidation()
        {
            TimelineAbilityDefinition ability = NewAbility("test.window.invalid", recoveryEndFrame: 20);
            ability.FirstStepForTests.SetTagWindowsForTests(new AbilityStepTagWindow(default, 1, 5));

            Assert.IsFalse(ability.TryValidate(out string error));
            Assert.IsTrue(error.Contains("tag window"), error);
        }

        [Test]
        public void WindowOutsideTheTimeline_FailsValidation()
        {
            TimelineAbilityDefinition ability = NewAbility("test.window.outside", recoveryEndFrame: 20);
            ability.FirstStepForTests.SetTagWindowsForTests(new AbilityStepTagWindow(Immune, 1, 99));

            Assert.IsFalse(ability.TryValidate(out string error));
            Assert.IsTrue(error.Contains("outside the timeline"), error);
        }

        [Test]
        public void WindowStartingOutsideTheTimeline_FailsValidation()
        {
            TimelineAbilityDefinition ability = NewAbility("test.window.start-outside", recoveryEndFrame: 20);
            ability.FirstStepForTests.SetTagWindowsForTests(new AbilityStepTagWindow(Immune, 99, 0));

            Assert.IsFalse(ability.TryValidate(out string error));
            Assert.IsTrue(error.Contains("starts outside the timeline"), error);
        }

        [Test]
        public void MissingSerializedTagWindows_AreTreatedAsEmpty()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition ability = NewAbility("test.window.legacy", recoveryEndFrame: 8);
            SetField<AbilityStepTagWindow[]>(ability.FirstStepForTests, "tagWindows", null);
            Grant(system, ability);

            Assert.IsTrue(ability.TryValidate(out string error), error);
            Assert.IsTrue(system.TryActivate(ability, AbilityContext.FromTarget(owner, null)));
            Assert.DoesNotThrow(() => TickToFrame(system, 2));
            Assert.IsEmpty(ability.FirstStepForTests.TagWindows);
        }

        // ------------------------------------------------------------ helpers

        /// <summary>
        /// Ticks until the instance reports the requested frame. Sub-frame
        /// steps, because one tick of exactly 1/60 lands past the frame-1
        /// boundary and would skip the frame being asserted on.
        /// </summary>
        private static void TickToFrame(AbilitySystem system, int frame)
        {
            for (int guard = 0; system.ActiveFrame < frame && guard < 2400; guard++)
            {
                system.Tick(Frame * 0.25f);
            }

            Assert.AreEqual(frame, system.ActiveFrame, "the tick loop landed off the frame");
        }

        private AbilitySystem NewSystem(out GameObject owner)
        {
            owner = new GameObject("TagWindowOwner");
            owned.Add(owner);
            return owner.AddComponent<AbilitySystem>();
        }

        private TimelineAbilityDefinition NewAbility(string abilityId, int recoveryEndFrame)
        {
            TimelineAbilityDefinition ability = ScriptableObject.CreateInstance<TimelineAbilityDefinition>();
            owned.Add(ability);
            ability.SetAbilityIdForTests(abilityId);
            SetField(ability, "requiresTarget", false);

            var step = new AbilityStep();
            step.ConfigureForTests(1, 2, recoveryEndFrame, 60f);
            ability.SetStepsForTests(step);
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

        private static void InvokeLifecycle(AbilitySystem system, string methodName)
        {
            MethodInfo method = typeof(AbilitySystem).GetMethod(
                methodName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, methodName);
            method.Invoke(system, null);
        }
    }
}
