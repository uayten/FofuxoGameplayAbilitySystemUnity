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
    }
}
