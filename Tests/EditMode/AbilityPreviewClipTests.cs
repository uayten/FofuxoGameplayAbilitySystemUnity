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
            AbilityDefinition ability = ScriptableObject.CreateInstance<AbilityDefinition>();
            try
            {
                ability.SetAnimationClipsForTests(null, null);
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
            AbilityDefinition ability = ScriptableObject.CreateInstance<AbilityDefinition>();
            AnimationClip clip = new AnimationClip();
            try
            {
                // The preview never shows the gameplay clip on its own: only
                // an explicitly assigned preview clip is shown.
                ability.SetAnimationClipsForTests(clip, null);
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
            AbilityDefinition ability = ScriptableObject.CreateInstance<AbilityDefinition>();
            AnimationClip clip = new AnimationClip();
            AnimationClip preview = new AnimationClip();
            try
            {
                ability.SetAnimationClipsForTests(clip, preview);
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

        [Test]
        public void HostedNativePreview_UsesFullClipRange()
        {
            AbilityDefinition ability =
                ScriptableObject.CreateInstance<AbilityDefinition>();
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
                ability.SetAnimationClipsForTests(null, preview);
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
        public void DerivedAbility_InheritsPreviewClip()
        {
            TargetAssistDefinition ability =
                ScriptableObject.CreateInstance<TargetAssistDefinition>();
            AnimationClip preview = new AnimationClip();
            try
            {
                ability.SetAnimationClipsForTests(null, preview);
                Assert.AreSame(preview, ability.PreviewClip);
                Assert.IsTrue(ability.HasAnimationPreview);
            }
            finally
            {
                Object.DestroyImmediate(preview);
                Object.DestroyImmediate(ability);
            }
        }

        [Test]
        public void PreviewClip_DoesNotAffectValidation()
        {
            AbilityDefinition ability = ScriptableObject.CreateInstance<AbilityDefinition>();
            AnimationClip preview = new AnimationClip();
            try
            {
                ability.SetAbilityIdForTests("test.preview");
                Assert.IsTrue(ability.TryValidate(out string before));
                ability.SetAnimationClipsForTests(null, preview);
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

            System.Reflection.FieldInfo field =
                target.GetType().GetField(fieldName, Flags);
            Assert.IsNotNull(
                field,
                $"Expected field '{fieldName}' on {target.GetType().FullName}.");
            return field.GetValue(target);
        }
    }
}
