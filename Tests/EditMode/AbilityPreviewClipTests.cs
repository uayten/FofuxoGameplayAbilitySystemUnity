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
        public void PreviewModel_DefaultsToNull()
        {
            AbilityDefinition ability = ScriptableObject.CreateInstance<AbilityDefinition>();
            try
            {
                // Null means the Inspector resolves the model from the clip's
                // parent folder instead.
                Assert.IsNull(ability.PreviewModel);
            }
            finally
            {
                Object.DestroyImmediate(ability);
            }
        }

        [Test]
        public void PreviewModelOverride_IsReturned()
        {
            AbilityDefinition ability = ScriptableObject.CreateInstance<AbilityDefinition>();
            GameObject model = new GameObject("PreviewModel");
            try
            {
                ability.SetPreviewModelForTests(model);
                Assert.AreSame(model, ability.PreviewModel);
            }
            finally
            {
                Object.DestroyImmediate(model);
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
