using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Fofuxo.GameplayAbilitySystem.Editor;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    /// <summary>
    /// The readings behind the Ability Debugger window. The one that matters
    /// most: its activation audit is the runtime's own verdict, not a second
    /// copy of the rules.
    /// </summary>
    public sealed class AbilityDebuggerModelTests
    {
        private GameObject owner;
        private GameObject target;
        private AbilitySystem system;
        private readonly List<Object> owned = new();

        [SetUp]
        public void SetUp()
        {
            owner = new GameObject("ModelOwner");
            target = new GameObject("ModelTarget");
            target.transform.position = owner.transform.position + Vector3.forward;
            system = owner.AddComponent<AbilitySystem>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object item in owned)
            {
                if (item != null)
                {
                    Object.DestroyImmediate(item);
                }
            }

            owned.Clear();
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(owner);
        }

        [Test]
        public void AuditActivations_ReportsWhatEvaluateActivationReports()
        {
            AbilityDefinition ready = NewAbility("test.ready");
            AbilityDefinition targeted = NewAbility("test.targeted");
            SetField(targeted, "requiresTarget", true);
            GrantAll(ready, targeted);
            List<AbilityDebuggerModel.ActivationReading> readings = new();

            AbilityDebuggerModel.AuditActivations(system, null, readings);

            Assert.AreEqual(2, readings.Count);
            AbilityContext context = AbilityContext.FromTarget(owner, null);
            for (int i = 0; i < readings.Count; i++)
            {
                AbilityActivationResult expected =
                    system.EvaluateActivation(readings[i].Ability, context);
                Assert.AreEqual(expected, readings[i].Result, readings[i].Ability.AbilityId);
            }

            Assert.IsTrue(readings[0].Result.IsAccepted);
            Assert.AreEqual(AbilityActivationRejection.MissingTarget, readings[1].Result.Rejection);
        }

        [Test]
        public void AuditActivations_UsesTheTargetItIsGiven()
        {
            AbilityDefinition targeted = NewAbility("test.targeted");
            SetField(targeted, "requiresTarget", true);
            GrantAll(targeted);
            List<AbilityDebuggerModel.ActivationReading> readings = new();

            AbilityDebuggerModel.AuditActivations(system, target, readings);

            Assert.IsTrue(readings[0].Result.IsAccepted, readings[0].Result.ToString());
        }

        [Test]
        public void AuditActivations_IsEmptyWithoutALoadout()
        {
            List<AbilityDebuggerModel.ActivationReading> readings = new()
            {
                default,
            };

            AbilityDebuggerModel.AuditActivations(system, null, readings);

            Assert.IsEmpty(readings);
        }

        [Test]
        public void AuditActivations_LeavesNoTraceInTheHistory()
        {
            bool previous = AbilityDiagnostics.Enabled;
            AbilityDiagnostics.Enabled = true;
            try
            {
                GrantAll(NewAbility("test.ready"));

                AbilityDebuggerModel.AuditActivations(
                    system, null, new List<AbilityDebuggerModel.ActivationReading>());

                Assert.IsFalse(system.HasHistory);
            }
            finally
            {
                AbilityDiagnostics.Enabled = previous;
            }
        }

        [Test]
        public void FormatEvent_NamesTheRejectionCode()
        {
            AbilityDefinition ability = NewAbility("test.cooling");
            AbilityEvent rejected = AbilityEvent.Rejected(
                ability,
                AbilityActivationResult.Rejected(
                    AbilityActivationRejection.OnCooldown, "still cooling"));

            string row = AbilityDebuggerModel.FormatEvent(in rejected, rejected.Time);

            StringAssert.Contains("ActivationRejected", row);
            StringAssert.Contains("OnCooldown", row);
            StringAssert.Contains("still cooling", row);
            StringAssert.Contains("test.cooling", row);
        }

        [Test]
        public void FormatEvent_NamesTheCancelTag()
        {
            AbilityDefinition ability = NewAbility("test.rolled");
            AbilityEvent cancelled = AbilityEvent.Cancelled(
                ability, 1, CommonGameplayTags.CancelManual, AbilityStepTransitionReason.Cancelled);

            string row = AbilityDebuggerModel.FormatEvent(in cancelled, cancelled.Time);

            StringAssert.Contains("AbilityCancelled", row);
            StringAssert.Contains("Cancel.Manual", row);
            StringAssert.Contains("step 2", row);
        }

        [Test]
        public void PassesFilter_HonoursTheMask()
        {
            AbilityDefinition ability = NewAbility("test.filtered");
            AbilityEvent rejected = AbilityEvent.Rejected(
                ability,
                AbilityActivationResult.Rejected(AbilityActivationRejection.NotGranted, "no"));
            AbilityEvent cancelled = AbilityEvent.Cancelled(
                ability, 0, CommonGameplayTags.CancelManual, AbilityStepTransitionReason.Cancelled);
            int onlyRejections = 1 << (int)AbilityEventKind.ActivationRejected;

            Assert.IsTrue(AbilityDebuggerModel.PassesFilter(in rejected, onlyRejections));
            Assert.IsFalse(AbilityDebuggerModel.PassesFilter(in cancelled, onlyRejections));
            Assert.IsTrue(AbilityDebuggerModel.PassesFilter(in cancelled, AbilityDebuggerModel.AllKinds));
        }

        [Test]
        public void KindNames_CoverTheEnum_ForTheMaskField()
        {
            Assert.AreEqual(
                System.Enum.GetValues(typeof(AbilityEventKind)).Length,
                AbilityDebuggerModel.KindNames.Length);
            Assert.Less(AbilityDebuggerModel.KindNames.Length, 32, "a MaskField holds 32 bits");
        }

        [Test]
        public void CollectActors_FindsEverySystem_SortedByName()
        {
            GameObject second = Own(new GameObject("ZZ_ModelActor"));
            AbilitySystem later = second.AddComponent<AbilitySystem>();
            GameObject first = Own(new GameObject("AA_ModelActor"));
            AbilitySystem earlier = first.AddComponent<AbilitySystem>();
            List<AbilitySystem> actors = new();

            AbilityDebuggerModel.CollectActors(actors);

            CollectionAssert.Contains(actors, system);
            CollectionAssert.Contains(actors, earlier);
            CollectionAssert.Contains(actors, later);
            Assert.Less(actors.IndexOf(earlier), actors.IndexOf(later));
        }

        [Test]
        public void DescribeReadiness_ReportsCooldownAndCharges()
        {
            AbilityDefinition ability = NewAbility("test.charged");
            SetField(ability, "cooldown", 3600f);
            SetField(ability, "maxCharges", 2);
            SetField(ability, "chargeRestoreTime", 3600f);
            GrantAll(ability);

            Assert.AreEqual("ready · charges 2/2", AbilityDebuggerModel.DescribeReadiness(system, ability));

            Assert.IsTrue(system.TryActivate(ability, AbilityContext.FromTarget(owner, null)));
            system.ForceCancelActiveAbility(CommonGameplayTags.CancelManual);

            string readiness = AbilityDebuggerModel.DescribeReadiness(system, ability);
            StringAssert.StartsWith("cooldown", readiness);
            StringAssert.Contains("charges 1/2", readiness);
        }

        [Test]
        public void DescribeAttribute_PrintsCurrentBaseAndLimits()
        {
            AttributeValue value = new(50f, 0f, 100f);

            string described = AbilityDebuggerModel.DescribeAttribute(value);

            StringAssert.StartsWith("50", described);
            StringAssert.Contains("base 50", described);
            StringAssert.Contains("0..100", described);
        }

        [Test]
        public void DescribeInstance_ForAPlainAbility_SaysNoTimeline()
        {
            AbilityDefinition ability = NewAbility("test.plain");
            GrantAll(ability);
            Assert.IsTrue(system.TryActivate(ability, AbilityContext.FromTarget(owner, null)));

            string described = AbilityDebuggerModel.DescribeInstance(system.ActiveInstances[0]);

            StringAssert.StartsWith("test.plain", described);
            StringAssert.Contains("no timeline", described);
        }

        [Test]
        public void DescribeTags_ListsTheActiveTags()
        {
            Assert.AreEqual("(none)", AbilityDebuggerModel.DescribeTags(system));

            system.SetLooseTag(CommonGameplayTags.Blocking, true);

            Assert.AreEqual("State.Blocking", AbilityDebuggerModel.DescribeTags(system));
        }

        private AbilityDefinition NewAbility(string id)
        {
            AbilityDefinition ability = Own(ScriptableObject.CreateInstance<AbilityDefinition>());
            SetField(ability, "abilityId", id);
            SetField(ability, "requiresTarget", false);
            return ability;
        }

        private void GrantAll(params AbilityDefinition[] abilities)
        {
            AbilityLoadout loadout = Own(ScriptableObject.CreateInstance<AbilityLoadout>());
            SetField(loadout, "abilities", abilities);
            SetField(system, "loadout", loadout);
        }

        private T Own<T>(T item) where T : Object
        {
            owned.Add(item);
            return item;
        }

        private static void SetField<TValue>(object target, string fieldName, TValue value)
        {
            for (System.Type type = target.GetType(); type != null; type = type.BaseType)
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
