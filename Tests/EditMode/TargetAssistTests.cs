using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Fofuxo.GameplayAbilitySystem;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    public sealed class TargetAssistTests
    {
        /// <summary>Makes a collider an actor; the assist accepts any living actor on its layers.</summary>
        private sealed class StubReceiver : AttributeSet
        {
        }

        [Test]
        public void ActivationSnapsOwnerTowardDamageableTarget()
        {
            GameObject owner = new("AssistOwner");
            GameObject target = new("AssistTarget");
            TimelineAbilityDefinition attack = ScriptableObject.CreateInstance<TimelineAbilityDefinition>();
            TargetAssistDefinition assist = ScriptableObject.CreateInstance<TargetAssistDefinition>();
            AbilityLoadout loadout = ScriptableObject.CreateInstance<AbilityLoadout>();
            try
            {
                target.transform.position = new Vector3(2f, 0f, 3f);
                target.AddComponent<BoxCollider>();
                target.AddComponent<StubReceiver>();

                SetField(attack, "abilityId", "test.assist.attack");
                SetField(attack, "requiresTarget", false);
                SetField(assist, "targetLayers", MakeMask(1 << target.layer));
                SetField(assist, "searchDistance", 5f);
                SetField(assist, "coneHalfAngle", 90f);
                SetField(assist, "proximityRadius", 0f);
                attack.SetTargetAssistForTests(assist);
                SetField(loadout, "abilities", new[] { attack });

                // The assist approaches by default, so the owner needs somewhere for that travel to land.
                owner.AddComponent<Rigidbody>();
                AbilitySystem system = owner.AddComponent<AbilitySystem>();
                SetField(system, "loadout", loadout);
                Physics.SyncTransforms();

                Assert.IsTrue(system.TryActivate(
                    attack, AbilityContext.FromTarget(owner, null)));

                Vector3 expected = (target.transform.position - owner.transform.position).normalized;
                expected.y = 0f;
                Assert.Less(Vector3.Angle(owner.transform.forward, expected), 3f);
                Assert.AreEqual(target, system.ActiveContext?.Target);
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(attack);
                Object.DestroyImmediate(assist);
                Object.DestroyImmediate(loadout);
            }
        }

        [Test]
        public void ActivationWithoutTargetKeepsFacing()
        {
            GameObject owner = new("AssistOwner");
            TimelineAbilityDefinition attack = ScriptableObject.CreateInstance<TimelineAbilityDefinition>();
            TargetAssistDefinition assist = ScriptableObject.CreateInstance<TargetAssistDefinition>();
            AbilityLoadout loadout = ScriptableObject.CreateInstance<AbilityLoadout>();
            try
            {
                SetField(attack, "abilityId", "test.assist.attack");
                SetField(attack, "requiresTarget", false);
                SetField(assist, "targetLayers", MakeMask(-1));
                SetField(assist, "searchDistance", 5f);
                attack.SetTargetAssistForTests(assist);
                SetField(loadout, "abilities", new[] { attack });

                // The assist approaches by default, so the owner needs somewhere for that travel to land.
                owner.AddComponent<Rigidbody>();
                AbilitySystem system = owner.AddComponent<AbilitySystem>();
                SetField(system, "loadout", loadout);
                Vector3 before = owner.transform.forward;

                Assert.IsTrue(system.TryActivate(
                    attack, AbilityContext.FromTarget(owner, null)));
                Assert.AreEqual(before, owner.transform.forward);
                Assert.IsNull(system.ActiveContext?.Target);
                Assert.IsFalse(system.HasActiveDisplacement);
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(attack);
                Object.DestroyImmediate(assist);
                Object.DestroyImmediate(loadout);
            }
        }

        [Test]
        public void ZeroSearchDistanceUsesTwiceProximityRadius()
        {
            TargetAssistDefinition assist =
                ScriptableObject.CreateInstance<TargetAssistDefinition>();
            try
            {
                SetField(assist, "searchDistance", 0f);
                SetField(assist, "proximityRadius", 4f);

                Assert.AreEqual(8f, assist.ResolveSearchDistance());
            }
            finally
            {
                Object.DestroyImmediate(assist);
            }
        }

        [Test]
        public void ProximityRadiusSelectsTargetOutsideTheForwardCone()
        {
            GameObject owner = new("AssistOwner");
            GameObject target = new("AssistTarget");
            TimelineAbilityDefinition attack = ScriptableObject.CreateInstance<TimelineAbilityDefinition>();
            TargetAssistDefinition assist = ScriptableObject.CreateInstance<TargetAssistDefinition>();
            AbilityLoadout loadout = ScriptableObject.CreateInstance<AbilityLoadout>();
            try
            {
                target.transform.position = Vector3.back * 3f;
                target.AddComponent<BoxCollider>();
                target.AddComponent<StubReceiver>();

                SetField(attack, "abilityId", "test.assist.attack");
                SetField(attack, "requiresTarget", false);
                SetField(assist, "targetLayers", MakeMask(1 << target.layer));
                SetField(assist, "searchDistance", 0f);
                SetField(assist, "proximityRadius", 4f);
                SetField(assist, "coneHalfAngle", 35f);
                attack.SetTargetAssistForTests(assist);
                SetField(loadout, "abilities", new[] { attack });

                // The assist approaches by default, so the owner needs somewhere for that travel to land.
                owner.AddComponent<Rigidbody>();
                AbilitySystem system = owner.AddComponent<AbilitySystem>();
                SetField(system, "loadout", loadout);
                Physics.SyncTransforms();

                Assert.IsTrue(system.TryActivate(
                    attack, AbilityContext.FromDirection(owner, null, Vector3.forward)));
                Assert.AreEqual(target, system.ActiveContext?.Target);
                Assert.Less(Vector3.Angle(owner.transform.forward, Vector3.back), 3f);
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(attack);
                Object.DestroyImmediate(assist);
                Object.DestroyImmediate(loadout);
            }
        }

        [Test]
        public void SelectedDistantTargetStartsApproachDuringParentStartup()
        {
            GameObject owner = new("AssistOwner");
            GameObject target = new("AssistTarget");
            TimelineAbilityDefinition attack = ScriptableObject.CreateInstance<TimelineAbilityDefinition>();
            TargetAssistDefinition assist = ScriptableObject.CreateInstance<TargetAssistDefinition>();
            AbilityLoadout loadout = ScriptableObject.CreateInstance<AbilityLoadout>();
            try
            {
                owner.AddComponent<Rigidbody>();
                target.transform.position = Vector3.forward * 7f;
                target.AddComponent<BoxCollider>();
                target.AddComponent<StubReceiver>();

                SetField(attack, "abilityId", "test.assist.attack");
                SetField(attack, "requiresTarget", false);
                attack.FirstStepForTests.ConfigureForTests(20, 21, 60, 60f);
                SetField(assist, "targetLayers", MakeMask(1 << target.layer));
                SetField(assist, "searchDistance", 0f);
                SetField(assist, "proximityRadius", 4f);
                SetField(assist, "approachTarget", true);
                SetField(assist, "stoppingGap", 0f);
                attack.SetTargetAssistForTests(assist);
                SetField(loadout, "abilities", new[] { attack });

                AbilitySystem system = owner.AddComponent<AbilitySystem>();
                SetField(system, "loadout", loadout);
                Physics.SyncTransforms();

                Assert.IsTrue(system.TryActivate(
                    attack, AbilityContext.FromDirection(owner, null, Vector3.forward)));
                Assert.AreEqual(target, system.ActiveContext?.Target);
                Assert.IsTrue(system.HasActiveDisplacement);
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(attack);
                Object.DestroyImmediate(assist);
                Object.DestroyImmediate(loadout);
            }
        }

        [Test]
        public void ApproachAndParentDisplacementCannotShareOneAbility()
        {
            TimelineAbilityDefinition attack = ScriptableObject.CreateInstance<TimelineAbilityDefinition>();
            TargetAssistDefinition assist = ScriptableObject.CreateInstance<TargetAssistDefinition>();
            try
            {
                SetField(attack, "abilityId", "test.assist.attack");
                SetField(assist, "approachTarget", true);
                attack.SetTargetAssistForTests(assist);
                attack.FirstStepForTests.ConfigureDisplacementForTests(
                    AbilityDisplacementDirection.Context,
                    1f,
                    1,
                    2);

                Assert.IsFalse(attack.TryValidate(out string error));
                StringAssert.Contains("displacement", error);
            }
            finally
            {
                Object.DestroyImmediate(attack);
                Object.DestroyImmediate(assist);
            }
        }

        [Test]
        public void ApproachStopsWhenTheTwoBodiesAlmostTouch()
        {
            GameObject owner = new("AssistOwner");
            GameObject target = new("AssistTarget");
            TimelineAbilityDefinition attack = ScriptableObject.CreateInstance<TimelineAbilityDefinition>();
            TargetAssistDefinition assist = ScriptableObject.CreateInstance<TargetAssistDefinition>();
            AbilityLoadout loadout = ScriptableObject.CreateInstance<AbilityLoadout>();
            try
            {
                owner.AddComponent<Rigidbody>();
                owner.AddComponent<SphereCollider>().radius = 1f;
                target.transform.position = Vector3.forward * 7f;
                target.AddComponent<SphereCollider>().radius = 0.5f;
                target.AddComponent<StubReceiver>();

                SetField(attack, "abilityId", "test.assist.attack");
                SetField(attack, "requiresTarget", false);
                attack.FirstStepForTests.ConfigureForTests(20, 21, 60, 60f);
                SetField(assist, "targetLayers", MakeMask(1 << target.layer));
                SetField(assist, "searchDistance", 12f);
                SetField(assist, "proximityRadius", 0f);
                SetField(assist, "coneHalfAngle", 45f);
                SetField(assist, "approachTarget", true);
                SetField(assist, "stoppingGap", 0.05f);
                attack.SetTargetAssistForTests(assist);
                SetField(loadout, "abilities", new[] { attack });

                AbilitySystem system = owner.AddComponent<AbilitySystem>();
                SetField(system, "loadout", loadout);
                Physics.SyncTransforms();

                Assert.IsTrue(system.TryActivate(
                    attack, AbilityContext.FromDirection(owner, null, Vector3.forward)));

                // 7 m apart, minus the target's 0.5 surface, the owner's own 1.0
                // and the 0.05 gap left between them.
                Assert.AreEqual(5.45f, system.PlannedDisplacementDistanceForTests, 0.02f);
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(attack);
                Object.DestroyImmediate(assist);
                Object.DestroyImmediate(loadout);
            }
        }

        [Test]
        public void EveryStepWithAnAssistReacquiresItsOwnTarget()
        {
            GameObject owner = new("AssistOwner");
            GameObject first = new("FirstTarget");
            GameObject second = new("SecondTarget");
            TimelineAbilityDefinition combo = ScriptableObject.CreateInstance<TimelineAbilityDefinition>();
            TargetAssistDefinition assist = ScriptableObject.CreateInstance<TargetAssistDefinition>();
            AbilityLoadout loadout = ScriptableObject.CreateInstance<AbilityLoadout>();
            try
            {
                owner.AddComponent<Rigidbody>();
                first.transform.position = Vector3.forward * 2f;
                first.AddComponent<SphereCollider>().radius = 0.5f;
                first.AddComponent<StubReceiver>();
                AbilitySystem firstSystem = first.AddComponent<AbilitySystem>();
                second.transform.position = Vector3.right * 3f;
                second.AddComponent<SphereCollider>().radius = 0.5f;
                second.AddComponent<StubReceiver>();

                SetField(combo, "abilityId", "test.assist.combo");
                SetField(combo, "requiresTarget", false);
                SetField(assist, "targetLayers", MakeMask(1 << first.layer));
                SetField(assist, "searchDistance", 12f);
                SetField(assist, "proximityRadius", 12f);
                SetField(assist, "approachTarget", false);

                AbilityStep stepOne = new();
                stepOne.ConfigureForTests(1, 2, 3, 60f);
                stepOne.SetTargetAssistForTests(assist);
                AbilityStep stepTwo = new();
                stepTwo.ConfigureForTests(1, 2, 3, 60f);
                stepTwo.SetTargetAssistForTests(assist);
                combo.SetStepsForTests(stepOne, stepTwo);
                SetField(loadout, "abilities", new[] { combo });

                AbilitySystem system = owner.AddComponent<AbilitySystem>();
                SetField(system, "loadout", loadout);
                Physics.SyncTransforms();

                Assert.IsTrue(system.TryActivate(
                    combo, AbilityContext.FromDirection(owner, null, Vector3.forward)));
                Assert.AreEqual(first, system.ActiveContext?.Target);

                // The first target stops being a valid one between the swings:
                // dead is a tag, and the filter skips whoever holds it.
                firstSystem.SetLooseTag(CommonGameplayTags.Dead, true);
                for (int i = 0; i < 4; i++)
                {
                    system.Tick(1f / 60f);
                }

                Assert.AreEqual(1, system.ActiveStepIndex);

                Assert.AreEqual(second, system.ActiveContext?.Target);
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
                Object.DestroyImmediate(combo);
                Object.DestroyImmediate(assist);
                Object.DestroyImmediate(loadout);
            }
        }

        [Test]
        public void ApproachAndDisplacementCannotShareOneStep()
        {
            TimelineAbilityDefinition attack = ScriptableObject.CreateInstance<TimelineAbilityDefinition>();
            TargetAssistDefinition assist = ScriptableObject.CreateInstance<TargetAssistDefinition>();
            try
            {
                SetField(attack, "abilityId", "test.assist.attack");
                SetField(assist, "approachTarget", true);
                attack.FirstStepForTests.SetTargetAssistForTests(assist);
                attack.FirstStepForTests.ConfigureDisplacementForTests(
                    AbilityDisplacementDirection.Context,
                    1f,
                    1,
                    2);

                Assert.IsFalse(attack.TryValidate(out string error));
                StringAssert.Contains("displacement", error);
            }
            finally
            {
                Object.DestroyImmediate(attack);
                Object.DestroyImmediate(assist);
            }
        }

        private static LayerMask MakeMask(int bits)
        {
            LayerMask mask = default;
            mask.value = bits;
            return mask;
        }

        private static void SetField<TTarget, TValue>(
            TTarget target,
            string fieldName,
            TValue value)
        {
            // Walks up the hierarchy: a private field of a base class is
            // invisible to a single GetField call, and an ability's own fields
            // sit one level above the timeline type most fixtures use.
            for (System.Type type = typeof(TTarget); type != null; type = type.BaseType)
            {
                System.Reflection.FieldInfo field = type.GetField(
                    fieldName,
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    field.SetValue(target, value);
                    return;
                }
            }

            Assert.Fail($"No field named {fieldName}.");
        }
    }
}
