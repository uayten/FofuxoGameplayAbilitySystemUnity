using NUnit.Framework;
using UnityEngine;
using Fofuxo.GameplayAbilitySystem;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    public sealed class CostsChargesTests
    {
        private static readonly GameplayAttribute Stamina = new("Combat.Stamina");

        private GameObject owner;
        private AbilitySystem system;
        private AttributeSet attributes;
        private readonly System.Collections.Generic.List<Object> owned = new();

        [SetUp]
        public void SetUp()
        {
            owner = new GameObject("CostsOwner");
            system = owner.AddComponent<AbilitySystem>();
            attributes = owner.AddComponent<AttributeSet>();
            attributes.SetInitialValues(new[]
            {
                Initial(Stamina, 100f, 0f, 100f),
            });
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
            Object.DestroyImmediate(owner);
        }

        [Test]
        public void CostEffect_IsCheckedBeforeActivation_AndPaidOnSuccess()
        {
            TimelineAbilityDefinition ability = NewAbility("test.costly");
            TestEffects.SetCost(owned, ability, Stamina, 30f);
            Grant(ability);

            AbilityContext context = AbilityContext.FromTarget(owner, null);
            Assert.IsTrue(system.TryActivate(ability, context));
            Assert.AreEqual(70f, attributes.GetCurrent(Stamina), 0.0001f);

            system.ForceCancelActiveAbility(CommonGameplayTags.CancelManual);
            Assert.IsTrue(system.TryActivate(ability, context));
            Assert.AreEqual(40f, attributes.GetCurrent(Stamina), 0.0001f);

            system.ForceCancelActiveAbility(CommonGameplayTags.CancelManual);
            Assert.IsTrue(system.TryActivate(ability, context));
            Assert.AreEqual(10f, attributes.GetCurrent(Stamina), 0.0001f);

            system.ForceCancelActiveAbility(CommonGameplayTags.CancelManual);
            Assert.IsFalse(system.CheckCost(ability));
            Assert.IsFalse(
                system.CanActivate(ability, context, out string reason));
            Assert.IsTrue(reason.Contains("Insufficient"), reason);
        }

        [Test]
        public void CostEffect_TheOwnerIsImmuneTo_IsFree()
        {
            TimelineAbilityDefinition ability = NewAbility("test.free");
            GameplayEffectDefinition cost = TestEffects.SetCost(owned, ability, Stamina, 500f);
            cost.SetTagsForTests(effect: new[] { new GameplayTag("Effect.Cost") });
            GameplayEffectDefinition immunity = TestEffects.Infinite(
                owned, Stamina, AttributeOperation.Add, new GameplayEffectMagnitude(0f));
            immunity.SetTagsForTests(immunity: new[] { new GameplayTag("Effect.Cost") });
            Grant(ability);

            system.ApplyGameplayEffect(immunity, AbilityContext.FromTarget(owner, owner));

            Assert.IsTrue(system.CheckCost(ability), "a cost the owner is immune to can always be paid");
            Assert.IsTrue(system.TryActivate(ability, AbilityContext.FromTarget(owner, null)));
            Assert.AreEqual(100f, attributes.GetCurrent(Stamina), 0.0001f);
        }

        [Test]
        public void PeriodicCost_PaysInstallments_AndEndsAtTheFirstItCannotPay()
        {
            AbilityDefinition ability = NewPlainAbility("test.channel");
            TestEffects.SetCost(owned, ability, Stamina, 40f, period: 1f);
            Grant(ability);

            GameplayTag endedWith = default;
            system.AbilityCancelled += (_, tag) => endedWith = tag;

            Assert.IsTrue(system.TryActivate(ability, AbilityContext.FromTarget(owner, null)));
            Assert.AreEqual(60f, attributes.GetCurrent(Stamina), 0.0001f, "first installment on commit");

            system.Tick(1f);
            Assert.AreEqual(20f, attributes.GetCurrent(Stamina), 0.0001f, "second installment");
            Assert.IsTrue(system.IsActive);

            system.Tick(1f);
            Assert.IsFalse(system.IsActive, "20 stamina cannot pay 40");
            Assert.AreEqual(CommonGameplayTags.CancelInsufficientCost, endedWith);
            Assert.AreEqual(20f, attributes.GetCurrent(Stamina), 0.0001f, "the unpaid installment took nothing");
        }

        [Test]
        public void PeriodicCost_RefusesActivation_WhenTheFirstInstallmentCannotBePaid()
        {
            AbilityDefinition ability = NewPlainAbility("test.steep");
            TestEffects.SetCost(owned, ability, Stamina, 60f, period: 0.1f);
            Grant(ability);
            AbilityContext context = AbilityContext.FromTarget(owner, null);

            attributes.ApplyInstantModifier(new AttributeModifier(Stamina, AttributeOperation.Override, 30f));

            AbilityActivationResult result = system.EvaluateActivation(ability, context);
            Assert.AreEqual(AbilityActivationRejection.InsufficientAttribute, result.Rejection);
            Assert.IsFalse(system.TryActivate(ability, context));
        }

        [Test]
        public void ManualCommit_PaysAndStartsTheCooldown_OnlyWhenTheAbilityCommits()
        {
            AbilityDefinition ability = NewPlainAbility("test.windup");
            TestEffects.SetCost(owned, ability, Stamina, 30f);
            TestEffects.SetCooldown(owned, ability, 5f);
            TestEffects.SetField(ability, "commitPolicy", AbilityCommitPolicy.Manual);
            Grant(ability);

            Assert.IsTrue(system.TryActivate(ability, AbilityContext.FromTarget(owner, null)));
            Assert.AreEqual(100f, attributes.GetCurrent(Stamina), 0.0001f, "nothing paid before the commit");
            Assert.IsFalse(system.IsOnCooldown(ability));

            Assert.IsTrue(system.TryCommitAbility(ability));
            Assert.AreEqual(70f, attributes.GetCurrent(Stamina), 0.0001f);
            Assert.IsTrue(system.IsOnCooldown(ability));
            Assert.IsTrue(system.TryCommitAbility(ability), "committing twice pays once");
            Assert.AreEqual(70f, attributes.GetCurrent(Stamina), 0.0001f);
        }

        [Test]
        public void ManualCommit_ThatCannotPay_EndsTheActivation()
        {
            AbilityDefinition ability = NewPlainAbility("test.late");
            TestEffects.SetCost(owned, ability, Stamina, 30f);
            TestEffects.SetField(ability, "commitPolicy", AbilityCommitPolicy.Manual);
            Grant(ability);

            GameplayTag endedWith = default;
            system.AbilityCancelled += (_, tag) => endedWith = tag;

            Assert.IsTrue(system.TryActivate(ability, AbilityContext.FromTarget(owner, null)));
            attributes.ApplyInstantModifier(new AttributeModifier(Stamina, AttributeOperation.Override, 10f));

            Assert.IsFalse(system.TryCommitAbility(ability));
            Assert.IsFalse(system.IsActive);
            Assert.AreEqual(CommonGameplayTags.CancelCommitFailed, endedWith);
        }

        [Test]
        public void Charges_AreConsumed_AndBlockWhenExhausted()
        {
            TimelineAbilityDefinition ability = NewAbility("test.charged");
            TestEffects.SetField(ability, "maxCharges", 1);
            TestEffects.SetField(ability, "chargeRestoreTime", 3600f);
            Grant(ability);

            AbilityContext context = AbilityContext.FromTarget(owner, null);
            Assert.IsTrue(system.TryActivate(ability, context));
            system.ForceCancelActiveAbility(CommonGameplayTags.CancelManual);
            Assert.IsFalse(
                system.CanActivate(ability, context, out string reason));
            Assert.IsTrue(reason.Contains("charges"), reason);
        }

        [Test]
        public void TryValidate_RejectsAHeldCost_APeriodWithoutCost_AndChargelessRestore()
        {
            TimelineAbilityDefinition heldCost = NewAbility("test.held.cost");
            TestEffects.SetField(heldCost, "costGameplayEffect",
                TestEffects.Infinite(owned, Stamina, AttributeOperation.Add, new GameplayEffectMagnitude(-1f)));
            Assert.IsFalse(heldCost.TryValidate(out string costError));
            Assert.IsTrue(costError.Contains("Instant"), costError);

            TimelineAbilityDefinition periodOnly = NewAbility("test.period.only");
            TestEffects.SetField(periodOnly, "costPeriod", 1f);
            Assert.IsFalse(periodOnly.TryValidate(out string periodError));
            Assert.IsTrue(periodError.Contains("Cost Period"), periodError);

            TimelineAbilityDefinition stranded = NewAbility("test.stranded");
            TestEffects.SetField(stranded, "maxCharges", 2);
            Assert.IsFalse(stranded.TryValidate(out string chargeError));
            Assert.IsTrue(chargeError.Contains("charges"), chargeError);
        }

        private TimelineAbilityDefinition NewAbility(string id)
        {
            TimelineAbilityDefinition ability = ScriptableObject.CreateInstance<TimelineAbilityDefinition>();
            owned.Add(ability);
            TestEffects.SetField(ability, "abilityId", id);
            TestEffects.SetField(ability, "requiresTarget", false);
            return ability;
        }

        /// <summary>An ability with no timeline, which runs until something ends it.</summary>
        private AbilityDefinition NewPlainAbility(string id)
        {
            AbilityDefinition ability = ScriptableObject.CreateInstance<AbilityDefinition>();
            owned.Add(ability);
            TestEffects.SetField(ability, "abilityId", id);
            TestEffects.SetField(ability, "requiresTarget", false);
            return ability;
        }

        private void Grant(AbilityDefinition ability)
        {
            AbilityLoadout loadout = ScriptableObject.CreateInstance<AbilityLoadout>();
            owned.Add(loadout);
            TestEffects.SetField(loadout, "abilities", new[] { ability });
            TestEffects.SetField(system, "loadout", loadout);
        }

        private static AttributeSet.InitialValue Initial(
            GameplayAttribute attribute, float baseValue, float minValue, float maxValue)
        {
            return new AttributeSet.InitialValue(attribute, baseValue, minValue, maxValue);
        }
    }
}
