using NUnit.Framework;
using UnityEngine;
using Fofuxo.GameplayAbilitySystem;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    public sealed class AbilityDefinitionValidationTests
    {
        [Test]
        public void FreshAbility_HasNoId_AndFailsValidation()
        {
            TimelineAbilityDefinition ability = ScriptableObject.CreateInstance<TimelineAbilityDefinition>();
            try
            {
                Assert.IsTrue(string.IsNullOrWhiteSpace(ability.AbilityId));
                Assert.IsFalse(ability.TryValidate(out string error));
                Assert.IsFalse(string.IsNullOrWhiteSpace(error));
            }
            finally
            {
                Object.DestroyImmediate(ability);
            }
        }

        [Test]
        public void DefaultFrames_MapToStartupActiveRecovery()
        {
            TimelineAbilityDefinition ability = ScriptableObject.CreateInstance<TimelineAbilityDefinition>();
            try
            {
                Assert.AreEqual(AbilityPhase.Startup, ability.FirstStepForTests.GetPhase(1));
                Assert.AreEqual(AbilityPhase.Active, ability.FirstStepForTests.GetPhase(2));
                Assert.AreEqual(AbilityPhase.Recovery, ability.FirstStepForTests.GetPhase(ability.FirstStepForTests.RecoveryEndFrame));
            }
            finally
            {
                Object.DestroyImmediate(ability);
            }
        }

        [Test]
        public void AnimationClip_ExtendsTimelineThroughItsFullDuration()
        {
            TimelineAbilityDefinition ability =
                ScriptableObject.CreateInstance<TimelineAbilityDefinition>();
            AnimationClip clip = new AnimationClip
            {
                frameRate = 60f,
            };
            clip.SetCurve(
                string.Empty,
                typeof(Transform),
                "m_LocalPosition.x",
                new AnimationCurve(
                    new Keyframe(0f, 0f),
                    new Keyframe(2.5f, 1f)));

            try
            {
                ability.SetAbilityIdForTests("test.full-animation");
                ability.FirstStepForTests.SetAnimationClipForTests(clip);
                ability.FirstStepForTests.ConfigureActionWindowsForTests(29, 36, 68);

                Assert.AreEqual(150, ability.FirstStepForTests.AnimationFrameCount);
                Assert.AreEqual(150, ability.FirstStepForTests.RecoveryEndFrame);
                Assert.AreEqual(2.5f, ability.FirstStepForTests.Duration, 0.0001f);
                Assert.IsTrue(ability.TryValidate(out string error), error);
            }
            finally
            {
                Object.DestroyImmediate(clip);
                Object.DestroyImmediate(ability);
            }
        }

        [Test]
        public void ManualEndFrame_CanExtendPastAnimation()
        {
            TimelineAbilityDefinition ability =
                ScriptableObject.CreateInstance<TimelineAbilityDefinition>();
            AnimationClip clip = new AnimationClip
            {
                frameRate = 60f,
            };
            clip.SetCurve(
                string.Empty,
                typeof(Transform),
                "m_LocalPosition.x",
                new AnimationCurve(
                    new Keyframe(0f, 0f),
                    new Keyframe(0.5f, 1f)));

            try
            {
                ability.FirstStepForTests.SetAnimationClipForTests(clip);

                Assert.AreEqual(30, ability.FirstStepForTests.AnimationFrameCount);
                Assert.AreEqual(60, ability.FirstStepForTests.RecoveryEndFrame);
            }
            finally
            {
                Object.DestroyImmediate(clip);
                Object.DestroyImmediate(ability);
            }
        }

        [Test]
        public void NullParryEffect_FailsValidation()
        {
            TimelineAbilityDefinition ability = ScriptableObject.CreateInstance<TimelineAbilityDefinition>();
            try
            {
                ability.SetAbilityIdForTests("test.parry");
                ability.SetParryEffectsForTests(null, null);
                Assert.IsFalse(ability.TryValidate(out string error));
                Assert.IsTrue(error.Contains("Parry"));
            }
            finally
            {
                Object.DestroyImmediate(ability);
            }
        }

        [Test]
        public void DefaultCancelPolicy_AcceptsAnyTag()
        {
            TimelineAbilityDefinition ability = ScriptableObject.CreateInstance<TimelineAbilityDefinition>();
            try
            {
                Assert.IsTrue(ability.CanBeCancelledBy(CommonGameplayTags.CancelManual));
                Assert.IsTrue(ability.CanBeCancelledBy(new GameplayTag("Cancel.Anything")));
                Assert.IsTrue(ability.CanBeCancelledBy(default));
            }
            finally
            {
                Object.DestroyImmediate(ability);
            }
        }

        [Test]
        public void ListedCancelTags_AcceptOnlyThemselves()
        {
            TimelineAbilityDefinition ability = ScriptableObject.CreateInstance<TimelineAbilityDefinition>();
            try
            {
                ability.SetCancellationForTests(
                    AbilityCancelPolicy.OnlyListedTags,
                    new GameplayTag("Cancel.Death"));

                Assert.IsTrue(ability.CanBeCancelledBy(new GameplayTag("Cancel.Death")));
                Assert.IsFalse(ability.CanBeCancelledBy(CommonGameplayTags.CancelManual));
                Assert.IsFalse(ability.CanBeCancelledBy(default));
            }
            finally
            {
                Object.DestroyImmediate(ability);
            }
        }

        [Test]
        public void CancelPolicyNothing_RefusesEveryTag()
        {
            TimelineAbilityDefinition ability = ScriptableObject.CreateInstance<TimelineAbilityDefinition>();
            try
            {
                ability.SetCancellationForTests(AbilityCancelPolicy.Nothing);

                Assert.IsFalse(ability.CanBeCancelledBy(CommonGameplayTags.CancelManual));
            }
            finally
            {
                Object.DestroyImmediate(ability);
            }
        }

        [Test]
        public void ListedCancelPolicyWithoutTags_FailsValidation()
        {
            TimelineAbilityDefinition ability = ScriptableObject.CreateInstance<TimelineAbilityDefinition>();
            try
            {
                ability.SetAbilityIdForTests("test.cancel.empty");
                ability.SetCancellationForTests(AbilityCancelPolicy.OnlyListedTags);

                Assert.IsFalse(ability.TryValidate(out string error));
                StringAssert.Contains("Only Listed Tags", error);
            }
            finally
            {
                Object.DestroyImmediate(ability);
            }
        }
    }
}
