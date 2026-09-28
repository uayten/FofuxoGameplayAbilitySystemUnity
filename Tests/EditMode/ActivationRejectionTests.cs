using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    /// <summary>
    /// Every rejection path must name itself. An AI that has to tell "still on
    /// cooldown" from "out of range" reads the code, never the message.
    /// </summary>
    public sealed class ActivationRejectionTests
    {
        private static readonly GameplayAttribute Stamina = new("Combat.Stamina");
        private static readonly GameplayTag Required = new("State.Stanced");
        private static readonly GameplayTag Blocking = new("State.Blocking");

        private GameObject owner;
        private GameObject target;
        private AbilitySystem system;
        private readonly List<Object> owned = new();

        [SetUp]
        public void SetUp()
        {
            owner = new GameObject("RejectionOwner");
            target = new GameObject("RejectionTarget");
            target.transform.position = owner.transform.position + Vector3.forward;
            system = owner.AddComponent<AbilitySystem>();
        }

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
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(owner);
        }

        [Test]
        public void GrantedAbility_IsAccepted_WithAnEmptyMessage()
        {
            TimelineAbilityDefinition ability = Grant(NewAbility("test.accepted"));

            AbilityActivationResult result = system.EvaluateActivation(ability, TargetlessContext());

            Assert.IsTrue(result.IsAccepted);
            Assert.AreEqual(AbilityActivationRejection.None, result.Rejection);
            Assert.AreEqual(string.Empty, result.Message);
        }

        [Test]
        public void NullAbility_ReportsNullAbility()
        {
            AssertRejected(null, TargetlessContext(), AbilityActivationRejection.NullAbility);
        }

        [Test]
        public void InvalidDefinition_ReportsInvalidDefinition_WithTheDefinitionError()
        {
            TimelineAbilityDefinition ability = NewAbility(string.Empty);
            Grant(ability);

            AbilityActivationResult result =
                system.EvaluateActivation(ability, TargetlessContext());

            Assert.AreEqual(AbilityActivationRejection.InvalidDefinition, result.Rejection);
            ability.TryValidate(out string definitionError);
            Assert.AreEqual(definitionError, result.Message);
        }

        [Test]
        public void UngrantedAbility_ReportsNotGranted()
        {
            TimelineAbilityDefinition granted = Grant(NewAbility("test.granted"));
            TimelineAbilityDefinition stranger = NewAbility("test.stranger");

            Assert.IsTrue(system.EvaluateActivation(granted, TargetlessContext()).IsAccepted);
            AssertRejected(stranger, TargetlessContext(), AbilityActivationRejection.NotGranted);
        }

        [Test]
        public void SecondActivation_ReportsAnotherAbilityActive()
        {
            TimelineAbilityDefinition ability = Grant(NewAbility("test.busy"));

            Assert.IsTrue(system.TryActivate(ability, TargetlessContext()));
            AssertRejected(
                ability, TargetlessContext(), AbilityActivationRejection.AnotherAbilityActive);
        }

        [Test]
        public void CooldownAfterActivation_ReportsOnCooldown()
        {
            TimelineAbilityDefinition ability = NewAbility("test.cooling");
            TestEffects.SetCooldown(owned, ability, 3600f);
            Grant(ability);

            Assert.IsTrue(system.TryActivate(ability, TargetlessContext()));
            system.ForceCancelActiveAbility(CommonGameplayTags.CancelManual);

            AssertRejected(ability, TargetlessContext(), AbilityActivationRejection.OnCooldown);
        }

        [Test]
        public void ExhaustedCharges_ReportNoChargesLeft()
        {
            TimelineAbilityDefinition ability = NewAbility("test.charged");
            SetField(ability, "maxCharges", 1);
            SetField(ability, "chargeRestoreTime", 3600f);
            Grant(ability);

            Assert.IsTrue(system.TryActivate(ability, TargetlessContext()));
            system.ForceCancelActiveAbility(CommonGameplayTags.CancelManual);

            AssertRejected(ability, TargetlessContext(), AbilityActivationRejection.NoChargesLeft);
        }

        [Test]
        public void UnpayableCost_ReportsInsufficientAttribute()
        {
            AttributeSet attributes = owner.AddComponent<AttributeSet>();
            attributes.SetInitialValues(new[]
            {
                new AttributeSet.InitialValue(Stamina, 10f, 0f, 100f),
            });

            TimelineAbilityDefinition ability = NewAbility("test.expensive");
            TestEffects.SetCost(owned, ability, Stamina, 30f);
            Grant(ability);

            AssertRejected(
                ability, TargetlessContext(), AbilityActivationRejection.InsufficientAttribute);
        }

        [Test]
        public void MissingRequiredTag_ReportsMissingRequiredTag()
        {
            TimelineAbilityDefinition ability = NewAbility("test.stanced");
            SetField(ability, "requiredTags", new[] { Required });
            Grant(ability);

            AssertRejected(
                ability, TargetlessContext(), AbilityActivationRejection.MissingRequiredTag);

            system.SetLooseTag(Required, true);
            Assert.IsTrue(system.EvaluateActivation(ability, TargetlessContext()).IsAccepted);
        }

        [Test]
        public void BlockedTag_ReportsBlockedByTag()
        {
            TimelineAbilityDefinition ability = NewAbility("test.blocked");
            SetField(ability, "blockedTags", new[] { Blocking });
            Grant(ability);

            Assert.IsTrue(system.EvaluateActivation(ability, TargetlessContext()).IsAccepted);

            system.SetLooseTag(Blocking, true);
            AssertRejected(ability, TargetlessContext(), AbilityActivationRejection.BlockedByTag);
        }

        [Test]
        public void MissingTarget_ReportsMissingTarget()
        {
            TimelineAbilityDefinition ability = NewAbility("test.targeted");
            SetField(ability, "requiresTarget", true);
            Grant(ability);

            AssertRejected(
                ability,
                AbilityContext.FromTarget(owner, null),
                AbilityActivationRejection.MissingTarget);
        }

        [Test]
        public void TargetPastMaximumRange_ReportsOutOfRange()
        {
            TimelineAbilityDefinition ability = NewAbility("test.ranged");
            SetField(ability, "requiresTarget", true);
            SetField(ability, "maximumRange", 3f);
            Grant(ability);

            target.transform.position = owner.transform.position + Vector3.forward * 10f;

            AssertRejected(
                ability,
                AbilityContext.FromTarget(owner, target),
                AbilityActivationRejection.OutOfRange);
        }

        [Test]
        public void TargetOutsideFacingAngle_ReportsOutsideFacingAngle()
        {
            TimelineAbilityDefinition ability = NewAbility("test.facing");
            SetField(ability, "requiresTarget", true);
            SetField(ability, "maximumFacingAngle", 45f);
            Grant(ability);

            // Inside range, but squarely behind the owner.
            target.transform.position = owner.transform.position - Vector3.forward * 2f;

            AssertRejected(
                ability,
                AbilityContext.FromTarget(owner, target),
                AbilityActivationRejection.OutsideFacingAngle);
        }

        [Test]
        public void CanActivate_ReportsTheSameMessageAsTheTypedResult()
        {
            TimelineAbilityDefinition ability = NewAbility("test.cooling");
            TestEffects.SetCooldown(owned, ability, 3600f);
            Grant(ability);

            Assert.IsTrue(system.TryActivate(ability, TargetlessContext()));
            system.ForceCancelActiveAbility(CommonGameplayTags.CancelManual);

            AbilityActivationResult result =
                system.EvaluateActivation(ability, TargetlessContext());
            Assert.IsFalse(system.CanActivate(ability, TargetlessContext(), out string message));
            Assert.AreEqual(result.Message, message);
        }

        [Test]
        public void TryActivate_ReportsWhyItDidNotStart()
        {
            TimelineAbilityDefinition ability = NewAbility("test.ungranted");

            Assert.IsFalse(
                system.TryActivate(
                    ability, TargetlessContext(), out AbilityActivationResult result));
            Assert.AreEqual(AbilityActivationRejection.NotGranted, result.Rejection);
            Assert.IsFalse(system.IsActive);
        }

        [Test]
        public void TryActivate_ReportsAcceptedWhenTheAbilityStarts()
        {
            TimelineAbilityDefinition ability = Grant(NewAbility("test.accepted"));

            Assert.IsTrue(
                system.TryActivate(
                    ability, TargetlessContext(), out AbilityActivationResult result));
            Assert.IsTrue(result.IsAccepted);
            Assert.AreSame(ability, system.ActiveAbility);
        }

        [Test]
        public void EvaluateActivation_IsSideEffectFree()
        {
            TimelineAbilityDefinition ability = NewAbility("test.untouched");
            TestEffects.SetCooldown(owned, ability, 3600f);
            SetField(ability, "maxCharges", 2);
            SetField(ability, "chargeRestoreTime", 3600f);
            Grant(ability);

            for (int i = 0; i < 5; i++)
            {
                Assert.IsTrue(system.EvaluateActivation(ability, TargetlessContext()).IsAccepted);
            }

            Assert.IsFalse(system.IsActive);
            Assert.IsFalse(system.IsOnCooldown(ability));
            Assert.IsTrue(system.TryActivate(ability, TargetlessContext()));
        }

        [Test]
        public void RejectionCodes_AreDistinctPerCause()
        {
            // Two failures that share no code is the whole point of the enum:
            // matching on the message would have made these indistinguishable
            // to anything but a human.
            TimelineAbilityDefinition cooling = NewAbility("test.cooling");
            TestEffects.SetCooldown(owned, cooling, 3600f);
            TimelineAbilityDefinition blocked = NewAbility("test.blocked");
            SetField(blocked, "blockedTags", new[] { Blocking });
            GrantAll(cooling, blocked);

            Assert.IsTrue(system.TryActivate(cooling, TargetlessContext()));
            system.ForceCancelActiveAbility(CommonGameplayTags.CancelManual);
            system.SetLooseTag(Blocking, true);

            Assert.AreNotEqual(
                system.EvaluateActivation(cooling, TargetlessContext()).Rejection,
                system.EvaluateActivation(blocked, TargetlessContext()).Rejection);
        }

        private void AssertRejected(
            AbilityDefinition ability,
            AbilityContext context,
            AbilityActivationRejection expected)
        {
            AbilityActivationResult result = system.EvaluateActivation(ability, context);

            Assert.IsFalse(result.IsAccepted, result.ToString());
            Assert.AreEqual(expected, result.Rejection, result.Message);
            Assert.IsNotEmpty(result.Message);
        }

        private AbilityContext TargetlessContext()
        {
            return AbilityContext.FromTarget(owner, null);
        }

        private TimelineAbilityDefinition NewAbility(string id)
        {
            TimelineAbilityDefinition ability = ScriptableObject.CreateInstance<TimelineAbilityDefinition>();
            owned.Add(ability);
            SetField(ability, "abilityId", id);
            SetField(ability, "requiresTarget", false);
            return ability;
        }

        private TimelineAbilityDefinition Grant(TimelineAbilityDefinition ability)
        {
            GrantAll(ability);
            return ability;
        }

        private void GrantAll(params AbilityDefinition[] abilities)
        {
            AbilityLoadout loadout = ScriptableObject.CreateInstance<AbilityLoadout>();
            owned.Add(loadout);
            SetField(loadout, "abilities", abilities);
            SetField(system, "loadout", loadout);
        }

        private static void SetField<TValue>(object target, string fieldName, TValue value)
        {
            // Walks up the hierarchy: a private field of a base class is
            // invisible to a single GetField call, and an ability's own fields
            // sit one level above the timeline type most fixtures use.
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
