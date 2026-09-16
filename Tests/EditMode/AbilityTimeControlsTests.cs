using NUnit.Framework;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    /// <summary>
    /// The editor's hit-stop and pause rules, pinned as a pure function of the
    /// recorded event and the settings — the part that would otherwise only be
    /// checked by entering Play Mode and swinging.
    /// </summary>
    public sealed class AbilityTimeControlsTests
    {
        private static readonly AbilityTimeControls.Settings Nothing = new(false, false, false);
        private static readonly AbilityTimeControls.Settings HitStopOnly = new(true, false, false);
        private static readonly AbilityTimeControls.Settings PauseOnRejection = new(false, true, false);
        private static readonly AbilityTimeControls.Settings PauseOnCancel = new(false, false, true);

        private AbilityDefinition ability;

        [SetUp]
        public void SetUp()
        {
            ability = ScriptableObject.CreateInstance<AbilityDefinition>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(ability);
        }

        [Test]
        public void AcceptedDelivery_Freezes_WhenHitStopIsOn()
        {
            AbilityEvent landed = AbilityEvent.Delivered(ability, null, null, true);

            Assert.AreEqual(
                AbilityTimeControls.Decision.Freeze,
                AbilityTimeControls.Decide(in landed, in HitStopOnly));
            Assert.AreEqual(
                AbilityTimeControls.Decision.None,
                AbilityTimeControls.Decide(in landed, in Nothing));
        }

        [Test]
        public void RefusedDelivery_NeverFreezes()
        {
            AbilityEvent refused = AbilityEvent.Delivered(ability, null, null, false);

            Assert.AreEqual(AbilityEventKind.EffectRefused, refused.Kind);
            Assert.AreEqual(
                AbilityTimeControls.Decision.None,
                AbilityTimeControls.Decide(in refused, in HitStopOnly));
        }

        [Test]
        public void Rejection_Pauses_OnlyWhenAsked()
        {
            AbilityEvent rejected = AbilityEvent.Rejected(
                ability,
                AbilityActivationResult.Rejected(AbilityActivationRejection.OnCooldown, "cooling"));

            Assert.AreEqual(
                AbilityTimeControls.Decision.Pause,
                AbilityTimeControls.Decide(in rejected, in PauseOnRejection));
            Assert.AreEqual(
                AbilityTimeControls.Decision.None,
                AbilityTimeControls.Decide(in rejected, in HitStopOnly));
        }

        [Test]
        public void Cancel_Pauses_ExceptOwnerTeardown()
        {
            AbilityEvent manual = AbilityEvent.Cancelled(
                ability, 0, CommonGameplayTags.CancelManual, AbilityStepTransitionReason.Cancelled);
            AbilityEvent teardown = AbilityEvent.Cancelled(
                ability, 0, CommonGameplayTags.CancelOwnerTeardown, AbilityStepTransitionReason.Cancelled);

            Assert.AreEqual(
                AbilityTimeControls.Decision.Pause,
                AbilityTimeControls.Decide(in manual, in PauseOnCancel));
            Assert.AreEqual(
                AbilityTimeControls.Decision.None,
                AbilityTimeControls.Decide(in teardown, in PauseOnCancel));
            Assert.AreEqual(
                AbilityTimeControls.Decision.None,
                AbilityTimeControls.Decide(in manual, in Nothing));
        }

        [Test]
        public void EverythingElse_IsIgnored_WhateverTheSettings()
        {
            AbilityTimeControls.Settings all = new(true, true, true);
            AbilityEvent started = AbilityEvent.Started(ability, 0, null);
            AbilityEvent cue = AbilityEvent.Cue(ability, new GameplayTag("Cue.Test"));
            AbilityEvent completed = AbilityEvent.Completed(
                ability, 0, AbilityStepTransitionReason.Completed);

            Assert.AreEqual(AbilityTimeControls.Decision.None, AbilityTimeControls.Decide(in started, in all));
            Assert.AreEqual(AbilityTimeControls.Decision.None, AbilityTimeControls.Decide(in cue, in all));
            Assert.AreEqual(AbilityTimeControls.Decision.None, AbilityTimeControls.Decide(in completed, in all));
        }

        [Test]
        public void Settings_ClampTheirRanges()
        {
            float previousScale = AbilityTimeControls.SlowMotionScale;
            float previousHitStop = AbilityTimeControls.HitStopSeconds;
            try
            {
                AbilityTimeControls.SlowMotionScale = 5f;
                Assert.AreEqual(1f, AbilityTimeControls.SlowMotionScale, 1e-6f);
                AbilityTimeControls.SlowMotionScale = 0f;
                Assert.AreEqual(AbilityTimeControls.MinimumScale, AbilityTimeControls.SlowMotionScale, 1e-6f);

                AbilityTimeControls.HitStopSeconds = 10f;
                Assert.AreEqual(
                    AbilityTimeControls.MaximumHitStopSeconds, AbilityTimeControls.HitStopSeconds, 1e-6f);
            }
            finally
            {
                AbilityTimeControls.SlowMotionScale = previousScale;
                AbilityTimeControls.HitStopSeconds = previousHitStop;
            }
        }

        /// <summary>
        /// Outside Play Mode the controls own nothing: the tests run in edit
        /// mode, so this is also what keeps them from touching the Editor's
        /// time scale.
        /// </summary>
        [Test]
        public void OutsidePlayMode_NothingIsEngaged()
        {
            Assert.IsFalse(AbilityTimeControls.IsEngaged);
            Assert.IsFalse(AbilityTimeControls.IsFrozen);
        }
    }
}
