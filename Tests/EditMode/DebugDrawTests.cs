using NUnit.Framework;
using UnityEngine;
using Fofuxo.GameplayAbilitySystem;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    /// <summary>
    /// Exercises the debug-draw path: box corner math and the effect
    /// definition through its public trigger path. Drawing itself is
    /// editor-only and verified visually in the host project.
    /// </summary>
    public sealed class DebugDrawTests
    {
        private readonly System.Collections.Generic.List<Object> owned = new();

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
        public void ComputeBoxCorners_ReturnsEightAxisAlignedCorners()
        {
            Vector3[] corners = AbilityDebugDraw.ComputeBoxCorners(
                Vector3.zero,
                Vector3.one,
                Quaternion.identity);

            Assert.AreEqual(8, corners.Length);
            Assert.Contains(new Vector3(-1f, -1f, -1f), corners);
            Assert.Contains(new Vector3(1f, -1f, -1f), corners);
            Assert.Contains(new Vector3(-1f, -1f, 1f), corners);
            Assert.Contains(new Vector3(1f, -1f, 1f), corners);
            Assert.Contains(new Vector3(-1f, 1f, -1f), corners);
            Assert.Contains(new Vector3(1f, 1f, -1f), corners);
            Assert.Contains(new Vector3(-1f, 1f, 1f), corners);
            Assert.Contains(new Vector3(1f, 1f, 1f), corners);
        }

        [Test]
        public void ComputeBoxCorners_FollowsCenterAndRotation()
        {
            Vector3[] corners = AbilityDebugDraw.ComputeBoxCorners(
                new Vector3(10f, 0f, 0f),
                new Vector3(1f, 0f, 0f),
                Quaternion.Euler(0f, 90f, 0f));

            // A 90-degree yaw maps local +X onto world -Z. Rotation uses
            // floats, so compare with tolerance instead of exact equality.
            Vector3[] expected =
            {
                new(10f, 0f, 1f),
                new(10f, 0f, -1f),
            };
            foreach (Vector3 corner in corners)
            {
                float distance = Mathf.Min(
                    Vector3.Distance(corner, expected[0]),
                    Vector3.Distance(corner, expected[1]));
                Assert.LessOrEqual(distance, 1e-4f, corner.ToString());
            }
        }

        [Test]
        public void DebugDrawEffect_ApplyRegistersNoHits()
        {
            GameObject owner = NewOwner();
            DebugDrawEffectDefinition effect = NewEffect();
            effect.ConfigureDrawForTests(HitShape.Sphere(Vector3.zero, 5f), Color.yellow, 1f);

            Physics.SyncTransforms();
            AbilitySystem system = owner.GetComponent<AbilitySystem>();
            TimelineAbilityDefinition definition = ScriptableObject.CreateInstance<TimelineAbilityDefinition>();
            owned.Add(definition);
            AbilityContext context = AbilityContext.FromTarget(owner, null);
            AbilityInstance instance = new(definition, context);
            effect.ApplyFrom(new GameplayEffectContext(system, instance, 0));

            Assert.AreEqual(0, instance.RegisteredHitCount);
        }

        [Test]
        public void DebugDraw_EnabledDefaultsTrueAndRestores()
        {
            bool previous = AbilityDebugDraw.Enabled;
            try
            {
                Assert.IsTrue(AbilityDebugDraw.Enabled);
                AbilityDebugDraw.Enabled = false;
                Assert.IsFalse(AbilityDebugDraw.Enabled);
            }
            finally
            {
                AbilityDebugDraw.Enabled = previous;
            }
        }

        /// <summary>
        /// The debug effect used to declare its own shape enum, centre, extents
        /// and radius. Copying a query's shape into it is what makes the drawing
        /// trustworthy, so the field has to be the same type the query uses.
        /// </summary>
        [Test]
        public void DebugDrawEffect_ReadsTheSameShapeTypeAsQueries()
        {
            DebugDrawEffectDefinition effect = NewEffect();
            HitShape authored = HitShape.Cone(1 << 3, 6f, 40f, 2f);
            effect.ConfigureDrawForTests(authored, Color.yellow, 1f);

            Assert.AreEqual(HitShapeKind.Cone, effect.Targeting.Shape.Kind);
            Assert.AreEqual(6f, effect.Targeting.Shape.Radius, 1e-4f);
            Assert.AreEqual(40f, effect.Targeting.Shape.ConeHalfAngle, 1e-4f);
            Assert.AreEqual(2f, effect.Targeting.Shape.ConeInnerRadius, 1e-4f);
            Assert.IsTrue(authored.Equals(effect.Targeting.Shape));
        }

        [Test]
        public void DebugDrawEffect_NullOwnerDoesNotThrow()
        {
            DebugDrawEffectDefinition effect = NewEffect();
            TimelineAbilityDefinition definition = ScriptableObject.CreateInstance<TimelineAbilityDefinition>();
            owned.Add(definition);
            AbilityContext context = AbilityContext.FromTarget(null, null);
            AbilityInstance instance = new(definition, context);

            Assert.DoesNotThrow(() => effect.ApplyFrom(
                new GameplayEffectContext(null, instance, 0)));
        }

        private GameObject NewOwner()
        {
            GameObject owner = new("DebugDrawOwner");
            owned.Add(owner);
            owner.AddComponent<AbilitySystem>();
            return owner;
        }

        private DebugDrawEffectDefinition NewEffect()
        {
            DebugDrawEffectDefinition effect =
                ScriptableObject.CreateInstance<DebugDrawEffectDefinition>();
            owned.Add(effect);
            return effect;
        }

    }
}
