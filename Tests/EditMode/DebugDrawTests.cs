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
            SetEffect(effect, "shape", 0);
            SetEffect(effect, "radius", 5f);
            SetEffect(effect, "duration", 1f);

            Physics.SyncTransforms();
            AbilitySystem system = owner.GetComponent<AbilitySystem>();
            AbilityDefinition definition = ScriptableObject.CreateInstance<AbilityDefinition>();
            owned.Add(definition);
            AbilityContext context = AbilityContext.FromTarget(owner, null);
            AbilityInstance instance = new(definition, context);
            effect.Apply(new AbilityEffectContext(system, instance, 0));

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

        [Test]
        public void DebugDrawEffect_NullOwnerDoesNotThrow()
        {
            DebugDrawEffectDefinition effect = NewEffect();
            AbilityDefinition definition = ScriptableObject.CreateInstance<AbilityDefinition>();
            owned.Add(definition);
            AbilityContext context = AbilityContext.FromTarget(null, null);
            AbilityInstance instance = new(definition, context);

            Assert.DoesNotThrow(() => effect.Apply(
                new AbilityEffectContext(null, instance, 0)));
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

        private static void SetEffect<TValue>(
            DebugDrawEffectDefinition effect, string fieldName, TValue value)
        {
            var field = typeof(DebugDrawEffectDefinition).GetField(
                fieldName,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(field, fieldName);
            object boxedValue = value;
            if (field.FieldType.IsEnum)
            {
                boxedValue = System.Enum.ToObject(field.FieldType, value);
            }

            field.SetValue(effect, boxedValue);
        }
    }
}
