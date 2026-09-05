using NUnit.Framework;
using UnityEngine;
using Fofuxo.GameplayAbilitySystem;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    public sealed class AbilityDisplacementTests
    {
        private const float Tolerance = 0.0001f;

        private static void AssertDirection(Vector3 expected, Vector3 actual)
        {
            Assert.AreEqual(1f, actual.magnitude, Tolerance, "direction must be normalized");
            Assert.Greater(Vector3.Dot(expected.normalized, actual), 1f - Tolerance);
        }

        [Test]
        public void FreshAbility_HasNoDisplacement_AndValidates()
        {
            AbilityDefinition ability = ScriptableObject.CreateInstance<AbilityDefinition>();
            try
            {
                ability.SetAbilityIdForTests("test.none");
                Assert.IsFalse(ability.HasDisplacement);
                Assert.AreEqual(0f, ability.DisplacementDurationSeconds);
                Assert.IsTrue(ability.TryValidate(out string error), error);
            }
            finally
            {
                Object.DestroyImmediate(ability);
            }
        }

        [Test]
        public void ConfiguredWindow_ResolvesDurationFromFallbackRate()
        {
            AbilityDefinition ability = ScriptableObject.CreateInstance<AbilityDefinition>();
            try
            {
                ability.SetAbilityIdForTests("test.dash");
                ability.ConfigureDisplacementForTests(
                    AbilityDisplacementDirection.Context,
                    15f,
                    1,
                    55);
                Assert.IsTrue(ability.HasDisplacement);
                Assert.AreEqual(54f / 60f, ability.DisplacementDurationSeconds, Tolerance);
                Assert.IsTrue(ability.TryValidate(out string error), error);
            }
            finally
            {
                Object.DestroyImmediate(ability);
            }
        }

        [Test]
        public void CollapsedWindow_FailsValidation()
        {
            AbilityDefinition ability = ScriptableObject.CreateInstance<AbilityDefinition>();
            try
            {
                ability.SetAbilityIdForTests("test.bad-window");
                ability.ConfigureDisplacementForTests(
                    AbilityDisplacementDirection.Context,
                    5f,
                    10,
                    10);
                Assert.IsFalse(ability.TryValidate(out string error));
                Assert.IsFalse(string.IsNullOrWhiteSpace(error));
            }
            finally
            {
                Object.DestroyImmediate(ability);
            }
        }

        [Test]
        public void WindowOutsideTimeline_FailsValidation()
        {
            AbilityDefinition ability = ScriptableObject.CreateInstance<AbilityDefinition>();
            try
            {
                ability.SetAbilityIdForTests("test.bad-window");
                ability.ConfigureDisplacementForTests(
                    AbilityDisplacementDirection.Context,
                    5f,
                    1,
                    ability.RecoveryEndFrame + 1);
                Assert.IsFalse(ability.TryValidate(out string error));
                Assert.IsFalse(string.IsNullOrWhiteSpace(error));
            }
            finally
            {
                Object.DestroyImmediate(ability);
            }
        }

        [Test]
        public void ResolveDirection_ContextMode_UsesContextDirection()
        {
            GameObject owner = new("DisplacementOwner");
            try
            {
                owner.transform.forward = Vector3.right;
                AbilityContext context = AbilityContext.FromDirection(
                    owner,
                    null,
                    Vector3.back);
                Vector3 direction = AbilityDisplacement.ResolveDirection(
                    AbilityDisplacementDirection.Context,
                    context);
                AssertDirection(Vector3.back, direction);
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void ResolveDirection_OwnerForward_IgnoresContext()
        {
            GameObject owner = new("DisplacementOwner");
            try
            {
                owner.transform.forward = Vector3.right;
                AbilityContext context = AbilityContext.FromDirection(
                    owner,
                    null,
                    Vector3.back);
                Vector3 direction = AbilityDisplacement.ResolveDirection(
                    AbilityDisplacementDirection.OwnerForward,
                    context);
                AssertDirection(Vector3.right, direction);
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void ResolveDirection_TargetModes_PointAlongOwnerTargetAxis()
        {
            GameObject owner = new("DisplacementOwner");
            GameObject target = new("DisplacementTarget");
            try
            {
                owner.transform.position = Vector3.zero;
                target.transform.position = new Vector3(0f, 5f, 4f);
                AbilityContext context = AbilityContext.FromTarget(owner, target);
                Vector3 toward = AbilityDisplacement.ResolveDirection(
                    AbilityDisplacementDirection.TowardTarget,
                    context);
                Vector3 away = AbilityDisplacement.ResolveDirection(
                    AbilityDisplacementDirection.AwayFromTarget,
                    context);
                AssertDirection(Vector3.forward, toward);
                AssertDirection(Vector3.back, away);
            }
            finally
            {
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void ResolveDirection_WithoutOwner_FallsBackToForward()
        {
            AbilityContext context = new(null, null, Vector3.zero, Vector3.zero);
            Vector3 direction = AbilityDisplacement.ResolveDirection(
                AbilityDisplacementDirection.TowardTarget,
                context);
            Assert.AreEqual(Vector3.forward, direction);
        }

        [Test]
        public void WindowDuration_RejectsDegenerateInput()
        {
            Assert.AreEqual(0f, AbilityDisplacement.WindowDurationSeconds(10, 10, 60f));
            Assert.AreEqual(0f, AbilityDisplacement.WindowDurationSeconds(20, 10, 60f));
            Assert.AreEqual(0f, AbilityDisplacement.WindowDurationSeconds(1, 55, 0f));
        }

        [Test]
        public void TickDisplacement_TravelsConfiguredDistance_AtConstantSpeed()
        {
            AbilityDefinition ability = ScriptableObject.CreateInstance<AbilityDefinition>();
            GameObject owner = new("DisplacementOwner");
            try
            {
                AbilityContext context = AbilityContext.FromDirection(owner, null, Vector3.right);
                AbilityInstance instance = new(ability, context);
                instance.BeginDisplacement(Vector3.right, null, 15f, 0.9f);

                Assert.IsTrue(instance.HasActiveDisplacement);
                bool moved = instance.TickDisplacement(0.45f, out Vector3 firstStep);
                Assert.IsTrue(moved);
                Assert.AreEqual(7.5f, firstStep.magnitude, 0.01f);
                AssertDirection(Vector3.right, firstStep.normalized);

                float travelled = firstStep.magnitude;
                while (instance.TickDisplacement(1f / 60f, out Vector3 step))
                {
                    travelled += step.magnitude;
                }

                Assert.AreEqual(15f, travelled, 0.01f);
                Assert.IsFalse(instance.HasActiveDisplacement);
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(ability);
            }
        }

        [Test]
        public void TickDisplacement_ZeroDeltaTime_MovesNothing()
        {
            AbilityDefinition ability = ScriptableObject.CreateInstance<AbilityDefinition>();
            GameObject owner = new("DisplacementOwner");
            try
            {
                AbilityContext context = AbilityContext.FromDirection(owner, null, Vector3.right);
                AbilityInstance instance = new(ability, context);
                instance.BeginDisplacement(Vector3.right, null, 5f, 0.5f);

                Assert.IsFalse(instance.TickDisplacement(0f, out Vector3 step));
                Assert.AreEqual(Vector3.zero, step);
                Assert.IsTrue(instance.HasActiveDisplacement);
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(ability);
            }
        }
    }
}
