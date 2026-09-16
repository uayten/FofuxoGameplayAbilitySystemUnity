using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Animations;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    public sealed class AbilityAnimationPlayerTests
    {
        private GameObject root;
        private GameObject bone;
        private Animator animator;
        private AbilityAnimationPlayer player;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("AnimationRoot");
            bone = new GameObject("Bone");
            bone.transform.SetParent(root.transform, false);
            animator = root.AddComponent<Animator>();
            player = new AbilityAnimationPlayer(animator);
        }

        [TearDown]
        public void TearDown()
        {
            player.Dispose();
            Object.DestroyImmediate(root);
        }

        [Test]
        public void Play_UsesAssignedClipAsAuthoritativeOutput()
        {
            AnimationClip clip = CreateConstantClip("AbilityClip", 7f);
            try
            {
                Assert.IsTrue(player.Play(clip, 0f));
                Assert.AreSame(clip, player.CurrentClip);
                Assert.AreEqual(
                    AnimationStreamSource.PreviousInputs,
                    player.StreamSource);

                player.EvaluateForTests(0f);

                Assert.AreEqual(
                    7f,
                    bone.transform.localPosition.x,
                    0.0001f);
            }
            finally
            {
                Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public void Play_CrossFadesBetweenAbilityClips()
        {
            AnimationClip first = CreateConstantClip("FirstAbilityClip", 4f);
            AnimationClip second = CreateConstantClip("SecondAbilityClip", 10f);
            try
            {
                player.Play(first, 0f);
                player.EvaluateForTests(0f);
                Assert.AreEqual(
                    4f,
                    bone.transform.localPosition.x,
                    0.0001f);

                player.Play(second, 1f);
                player.Tick(0.5f);
                player.EvaluateForTests(0f);

                Assert.AreSame(second, player.CurrentClip);
                Assert.AreEqual(
                    7f,
                    bone.transform.localPosition.x,
                    0.0001f);
            }
            finally
            {
                Object.DestroyImmediate(second);
                Object.DestroyImmediate(first);
            }
        }

        [Test]
        public void Stop_FadesBackAndReleasesClip()
        {
            AnimationClip clip = CreateConstantClip("AbilityClip", 7f);
            try
            {
                player.Play(clip, 0f);
                Assert.AreEqual(1f, player.OutputWeight, 0.0001f);

                player.Stop(0.2f);
                player.Tick(0.1f);
                Assert.AreEqual(0.5f, player.OutputWeight, 0.0001f);
                Assert.IsTrue(player.IsPlaying);

                player.Tick(0.1f);
                Assert.AreEqual(0f, player.OutputWeight, 0.0001f);
                Assert.IsFalse(player.IsPlaying);
                Assert.IsNull(player.CurrentClip);
            }
            finally
            {
                Object.DestroyImmediate(clip);
            }
        }

        private static AnimationClip CreateConstantClip(string name, float position)
        {
            AnimationClip clip = new AnimationClip
            {
                name = name,
                frameRate = 60f,
            };
            clip.SetCurve(
                "Bone",
                typeof(Transform),
                "m_LocalPosition.x",
                new AnimationCurve(
                    new Keyframe(0f, position),
                    new Keyframe(1f, position)));
            return clip;
        }
    }
}
