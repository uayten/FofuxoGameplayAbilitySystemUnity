using NUnit.Framework;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    /// <summary>
    /// The Unreal-shaped model an ability pays and waits in: a condition the
    /// game owns, Set By Caller numbers on the ability's own specs, effects that
    /// live as long as the activation, a cooldown that is an effect, effects
    /// switched off by Ongoing Tag Requirements, and the loadout's granted effects.
    /// </summary>
    public sealed class AbilityEffectModelTests : GameplayEffectTestBase
    {
        private static readonly GameplayAttribute Speed = new("Test.Speed");
        private static readonly GameplayAttribute Stamina = new("Test.Stamina");
        private static readonly GameplayTag SpeedData = new("Data.Test.Speed");
        private static readonly GameplayTag Sprinting = new("State.Test.Sprinting");

        private GameObject owner;
        private AbilitySystem system;
        private AttributeSet attributes;

        [SetUp]
        public void SetUp()
        {
            owner = NewActor(
                "ModelOwner",
                new AttributeSet.InitialValue(Speed, 1f, 0f, 100f),
                new AttributeSet.InitialValue(Stamina, 50f, 0f, 100f));
            system = owner.AddComponent<AbilitySystem>();
            attributes = Attributes(owner);
        }

        [TearDown]
        public void ResetDiagnostics()
        {
            AbilityDiagnostics.Enabled = true;
        }

        [Test]
        public void CanActivateAbility_RefusalIsConditionNotMet_AndAskingRecordsNothing()
        {
            RefusingAbility ability = NewAbility<RefusingAbility>("test.refusing");
            Grant(ability);
            AbilityDiagnostics.Enabled = true;
            AbilityContext context = AbilityContext.FromTarget(owner, null);

            AbilityActivationResult result = system.EvaluateActivation(ability, context);

            Assert.AreEqual(AbilityActivationRejection.ConditionNotMet, result.Rejection);
            Assert.AreEqual(RefusingAbility.Reason, result.Message);
            Assert.IsFalse(system.HasHistory, "a question leaves nothing in the history");
            Assert.IsFalse(system.TryActivate(ability, context));
        }

        [Test]
        public void ConfigureOutgoingSpec_FillsTheSetByCallerMagnitudeOfAnActiveEffect()
        {
            SpeedAbility ability = NewAbility<SpeedAbility>("test.sprint");
            GameplayEffectDefinition buff = TestEffects.Infinite(
                null,
                Speed,
                AttributeOperation.Multiply,
                new GameplayEffectMagnitude(0f).WithSetByCaller(SpeedData));
            Own(buff);
            TestEffects.SetField(ability, "activeEffects", new[] { buff });
            Grant(ability);

            Assert.IsTrue(system.TryActivate(ability, AbilityContext.FromTarget(owner, null)));
            Assert.AreEqual(1.5f, attributes.GetCurrent(Speed), 0.0001f);

            system.TryCancelAbility(ability, CommonGameplayTags.CancelManual);
            Assert.AreEqual(1f, attributes.GetCurrent(Speed), 0.0001f, "the Active Effect left with the activation");
            Assert.AreEqual(0, Effects(owner).ActiveEffectCount);
        }

        [Test]
        public void ActiveEffects_RefuseAnInstantEffect()
        {
            AbilityDefinition ability = NewAbility<AbilityDefinition>("test.instant.active");
            TestEffects.SetField(ability, "activeEffects", new[] { Own(TestEffects.Cost(null, Stamina, 1f)) });

            Assert.IsFalse(ability.TryValidate(out string error));
            StringAssert.Contains("instant", error);
        }

        [Test]
        public void Cooldown_IsAnEffect_WhoseGrantedTagsMeanOnCooldown()
        {
            AbilityDefinition ability = NewAbility<SelfEndingAbility>("test.cooling");
            GameplayEffectDefinition cooldown = TestEffects.SetCooldown(null, ability, 5f, "Cooldown.Test.Model");
            Own(cooldown);
            Grant(ability);

            Assert.IsTrue(system.TryActivate(ability, AbilityContext.FromTarget(owner, null)));
            Assert.IsTrue(system.IsOnCooldown(ability));
            Assert.IsTrue(system.HasTag(new GameplayTag("Cooldown.Test.Model")));

            Effects(owner).Tick(2f);
            system.GetCooldownTimeRemainingAndDuration(ability, out float remaining, out float duration);
            Assert.AreEqual(3f, remaining, 0.0001f);
            Assert.AreEqual(5f, duration, 0.0001f);

            Effects(owner).Tick(3f);
            Assert.IsFalse(system.IsOnCooldown(ability));
        }

        [Test]
        public void CooldownOnCompletion_StartsOnlyForACommittedActivation()
        {
            AbilityDefinition ability = NewAbility<SelfEndingAbility>("test.uncommitted");
            Own(TestEffects.SetCooldown(null, ability, 5f));
            TestEffects.SetField(ability, "cooldownStartPolicy", AbilityCooldownStartPolicy.OnCompletion);
            TestEffects.SetField(ability, "commitPolicy", AbilityCommitPolicy.Manual);
            Grant(ability);

            Assert.IsTrue(system.TryActivate(ability, AbilityContext.FromTarget(owner, null)));
            Assert.IsTrue(system.TryCompleteActiveAbility(ability));
            Assert.IsFalse(system.IsOnCooldown(ability), "never committed, so nothing to cool down from");

            Assert.IsTrue(system.TryActivate(ability, AbilityContext.FromTarget(owner, null)));
            Assert.IsTrue(system.TryCommitAbility(ability));
            Assert.IsFalse(system.IsOnCooldown(ability), "On Completion waits for the end");
            Assert.IsTrue(system.TryCompleteActiveAbility(ability));
            Assert.IsTrue(system.IsOnCooldown(ability));
        }

        [Test]
        public void OngoingBlockedTag_SwitchesAModifierEffectOff_AndBackOn()
        {
            GameplayEffectDefinition buff = NewEffect(
                GameplayEffectDurationPolicy.Infinite, new[] { Add(Stamina, 10f) });
            buff.SetOngoingTagsForTests(blocked: new[] { Sprinting });
            Apply(buff, owner, owner);
            Assert.AreEqual(60f, attributes.GetCurrent(Stamina), 0.0001f);

            system.SetLooseTag(Sprinting, true);
            Effects(owner).Tick(0.1f);
            Assert.AreEqual(50f, attributes.GetCurrent(Stamina), 0.0001f, "off while the tag is held");
            Assert.AreEqual(1, Effects(owner).ActiveEffectCount, "but still applied");

            system.SetLooseTag(Sprinting, false);
            Effects(owner).Tick(0.1f);
            Assert.AreEqual(60f, attributes.GetCurrent(Stamina), 0.0001f);
        }

        [Test]
        public void OngoingBlockedTag_PausesAPeriodicRegeneration()
        {
            GameplayEffectDefinition regen = NewEffect(
                GameplayEffectDurationPolicy.Infinite, new[] { Add(Stamina, 5f) }, 0f, 1f);
            SetExecuteOnApplication(regen, false);
            regen.SetOngoingTagsForTests(blocked: new[] { Sprinting });
            Apply(regen, owner, owner);

            Effects(owner).Tick(1f);
            Assert.AreEqual(55f, attributes.GetCurrent(Stamina), 0.0001f);

            system.SetLooseTag(Sprinting, true);
            Effects(owner).Tick(3f);
            Assert.AreEqual(55f, attributes.GetCurrent(Stamina), 0.0001f, "no refill while sprinting");

            system.SetLooseTag(Sprinting, false);
            Effects(owner).Tick(1f);
            Assert.AreEqual(60f, attributes.GetCurrent(Stamina), 0.0001f);
        }

        [Test]
        public void ApplyDynamic_OnTwoAttributes_KeepsBothModifiers()
        {
            GameplayEffectContainer container = Effects(owner);
            container.ApplyDynamic(
                GameplayEffectDurationPolicy.Infinite, Speed, AttributeOperation.Multiply, 1f, source: owner);
            container.ApplyDynamic(
                GameplayEffectDurationPolicy.Infinite, Stamina, AttributeOperation.Add, 10f, source: owner);

            Assert.AreEqual(2, container.ActiveEffectCount);
            Assert.AreEqual(2f, attributes.GetCurrent(Speed), 0.0001f);
            Assert.AreEqual(60f, attributes.GetCurrent(Stamina), 0.0001f);

            container.ApplyDynamic(
                GameplayEffectDurationPolicy.Infinite, Stamina, AttributeOperation.Add, 20f, source: owner);
            Assert.AreEqual(2, container.ActiveEffectCount, "the same attribute still refreshes");
            Assert.AreEqual(70f, attributes.GetCurrent(Stamina), 0.0001f);
        }

        [Test]
        public void LoadoutGrantedEffects_AreAppliedOnce()
        {
            GameplayEffectDefinition passive = NewEffect(
                GameplayEffectDurationPolicy.Infinite, new[] { Add(Stamina, 5f) });
            AbilityLoadout loadout = Own(ScriptableObject.CreateInstance<AbilityLoadout>());
            TestEffects.SetField(loadout, "grantedEffects", new[] { passive });
            TestEffects.SetField(system, "loadout", loadout);

            system.GrantLoadoutEffects();
            system.GrantLoadoutEffects();

            Assert.AreEqual(1, Effects(owner).ActiveEffectCount);
            Assert.AreEqual(55f, attributes.GetCurrent(Stamina), 0.0001f);
        }

        private T NewAbility<T>(string id) where T : AbilityDefinition
        {
            T ability = Own(ScriptableObject.CreateInstance<T>());
            ability.SetAbilityIdForTests(id);
            TestEffects.SetField(ability, "requiresTarget", false);
            TestEffects.SetField(ability, "lockMovementDuringAbility", false);
            return ability;
        }

        private void Grant(params AbilityDefinition[] abilities)
        {
            AbilityLoadout loadout = Own(ScriptableObject.CreateInstance<AbilityLoadout>());
            TestEffects.SetField(loadout, "abilities", abilities);
            TestEffects.SetField(system, "loadout", loadout);
        }

        private static void SetExecuteOnApplication(GameplayEffectDefinition effect, bool value)
        {
            TestEffects.SetField(effect, "executePeriodOnApplication", value);
        }

        private sealed class RefusingAbility : AbilityDefinition
        {
            public const string Reason = "The test says no.";

            protected internal override bool CanActivateAbility(
                AbilitySystem system, in AbilityContext context, out string reason)
            {
                reason = Reason;
                return false;
            }
        }

        private sealed class SpeedAbility : AbilityDefinition
        {
            protected internal override void ConfigureOutgoingSpec(GameplayEffectSpec spec)
            {
                spec.SetSetByCallerMagnitude(SpeedData, 0.5f);
            }
        }

        /// <summary>A plain ability the test ends by hand; the type silences the authoring warning.</summary>
        private sealed class SelfEndingAbility : AbilityDefinition
        {
        }
    }
}
