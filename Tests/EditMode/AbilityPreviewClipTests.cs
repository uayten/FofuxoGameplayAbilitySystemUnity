using NUnit.Framework;
using UnityEngine;
using Fofuxo.GameplayAbilitySystem;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    public sealed class AbilityPreviewClipTests
    {
        [Test]
        public void NoClips_HasNoPreview()
        {
            TimelineAbilityDefinition ability = ScriptableObject.CreateInstance<TimelineAbilityDefinition>();
            try
            {
                ability.FirstStepForTests.SetAnimationClipForTests(null);
                Assert.IsNull(ability.PreviewClip);
                Assert.IsFalse(ability.HasAnimationPreview);
            }
            finally
            {
                Object.DestroyImmediate(ability);
            }
        }

        [Test]
        public void GameplayClipAlone_ShowsNoPreview()
        {
            TimelineAbilityDefinition ability = ScriptableObject.CreateInstance<TimelineAbilityDefinition>();
            AnimationClip clip = new AnimationClip();
            try
            {
                // The preview never shows a step's gameplay clip on its own:
                // only the ability's own preview clip is shown. The Inspector
                // offers a button per step that copies one across.
                ability.FirstStepForTests.SetAnimationClipForTests(clip);
                Assert.IsNull(ability.PreviewClip);
                Assert.IsFalse(ability.HasAnimationPreview);
            }
            finally
            {
                Object.DestroyImmediate(clip);
                Object.DestroyImmediate(ability);
            }
        }

        [Test]
        public void AssignedPreviewClip_IsUsedAsPreview()
        {
            TimelineAbilityDefinition ability = ScriptableObject.CreateInstance<TimelineAbilityDefinition>();
            AnimationClip clip = new AnimationClip();
            AnimationClip preview = new AnimationClip();
            try
            {
                ability.FirstStepForTests.SetAnimationClipForTests(clip);
                ability.SetPreviewClipForTests(preview);
                Assert.AreSame(preview, ability.PreviewClip);
                Assert.IsTrue(ability.HasAnimationPreview);
            }
            finally
            {
                Object.DestroyImmediate(preview);
                Object.DestroyImmediate(clip);
                Object.DestroyImmediate(ability);
            }
        }

        /// <summary>
        /// One preview clip serves every step, so adding steps must not change
        /// what the preview panel plays.
        /// </summary>
        [Test]
        public void PreviewClip_IsSharedByEveryStep()
        {
            TimelineAbilityDefinition ability = ScriptableObject.CreateInstance<TimelineAbilityDefinition>();
            AnimationClip preview = new AnimationClip();
            try
            {
                ability.SetStepsForTests(
                    new AbilityStep(),
                    new AbilityStep(),
                    new AbilityStep(),
                    new AbilityStep());
                ability.SetPreviewClipForTests(preview);

                Assert.AreEqual(4, ability.StepCount);
                Assert.AreSame(preview, ability.PreviewClip);
            }
            finally
            {
                Object.DestroyImmediate(preview);
                Object.DestroyImmediate(ability);
            }
        }

        [Test]
        public void HostedNativePreview_UsesFullClipRange()
        {
            TimelineAbilityDefinition ability =
                ScriptableObject.CreateInstance<TimelineAbilityDefinition>();
            AnimationClip preview = new AnimationClip
            {
                frameRate = 60f,
            };
            preview.SetCurve(
                string.Empty,
                typeof(Transform),
                "m_LocalPosition.x",
                new AnimationCurve(
                    new Keyframe(0f, 0f),
                    new Keyframe(2.5f, 1f)));

            UnityEditor.Editor editor = null;
            try
            {
                ability.SetPreviewClipForTests(preview);
                editor = UnityEditor.Editor.CreateEditor(ability);

                Assert.IsTrue(editor.HasPreviewGUI());
                object clipEditor = GetRequiredField(editor, "previewClipEditor");
                Assert.AreEqual(
                    "UnityEditor.AnimationClipEditor",
                    clipEditor.GetType().FullName);

                object avatarPreview =
                    GetRequiredField(clipEditor, "m_AvatarPreview");
                object timeControl =
                    GetRequiredField(avatarPreview, "timeControl");
                float stopTime =
                    (float)GetRequiredField(timeControl, "stopTime");

                Assert.That(stopTime, Is.EqualTo(preview.length).Within(0.0001f));
                Assert.That(
                    stopTime * preview.frameRate,
                    Is.EqualTo(150f).Within(0.01f));
            }
            finally
            {
                if (editor != null)
                {
                    Object.DestroyImmediate(editor);
                }

                Object.DestroyImmediate(preview);
                Object.DestroyImmediate(ability);
            }
        }

        [Test]
        public void PreviewClip_DoesNotAffectValidation()
        {
            TimelineAbilityDefinition ability = ScriptableObject.CreateInstance<TimelineAbilityDefinition>();
            AnimationClip preview = new AnimationClip();
            try
            {
                ability.SetAbilityIdForTests("test.preview");
                Assert.IsTrue(ability.TryValidate(out string before));
                ability.SetPreviewClipForTests(preview);
                Assert.IsTrue(ability.TryValidate(out string after));
                Assert.AreSame(preview, ability.PreviewClip);
            }
            finally
            {
                Object.DestroyImmediate(preview);
                Object.DestroyImmediate(ability);
            }
        }

        private static object GetRequiredField(object target, string fieldName)
        {
            const System.Reflection.BindingFlags Flags =
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic;

            // Walks up the hierarchy: a private field of a base class is
            // invisible to a single GetField call, and an ability's own fields
            // sit one level above the timeline type most fixtures use.
            for (System.Type type = target.GetType(); type != null; type = type.BaseType)
            {
                System.Reflection.FieldInfo field = type.GetField(
                    fieldName, Flags | System.Reflection.BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    return field.GetValue(target);
                }
            }

            Assert.Fail(
                $"Expected field '{fieldName}' on {target.GetType().FullName}.");
            return null;
        }
    }
}
