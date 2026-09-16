using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    /// <summary>
    /// Ability groups and the mutual-exclusion policies around them. The first
    /// test is the load-bearing one: every ability authored before groups
    /// existed keeps the single-active-ability rule, because that is still the
    /// default.
    /// </summary>
    public sealed class AbilityGroupExclusionTests
    {
        private const float Tick = 1f / 60f;
        private static readonly GameplayTag Attacks = new("Group.Attacks");
        private static readonly GameplayTag Auras = new("Group.Auras");

        private readonly List<Object> owned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in owned)
            {
                if (o != null)
                {
                    Object.DestroyImmediate(o);
                }
            }

            owned.Clear();
        }

        [Test]
        public void ByDefault_OneAbilityRunsAtATime()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition first = NewAbility("test.group.default.first");
            TimelineAbilityDefinition second = NewAbility("test.group.default.second");
            Grant(system, first, second);

            Assert.IsTrue(system.TryActivate(first, AbilityContext.FromTarget(owner, null)));
            Assert.IsFalse(
                system.TryActivate(
                    second,
                    AbilityContext.FromTarget(owner, null),
                    out AbilityActivationResult result));

            Assert.AreEqual(AbilityActivationRejection.AnotherAbilityActive, result.Rejection);
            Assert.AreEqual(1, system.ActiveAbilityCount);
            Assert.AreEqual(first, system.ActiveAbility);
        }

        [Test]
        public void TheSameAbilityNeverRunsTwice()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition aura = NewAbility("test.group.twice");
            aura.SetExclusionGroupForTests(
                Auras, AbilityGroupExclusionPolicy.BlockWhileSameGroupActive);
            Grant(system, aura);

            Assert.IsTrue(system.TryActivate(aura, AbilityContext.FromTarget(owner, null)));
            Assert.IsFalse(
                system.TryActivate(
                    aura,
                    AbilityContext.FromTarget(owner, null),
                    out AbilityActivationResult result));

            Assert.AreEqual(AbilityActivationRejection.AnotherAbilityActive, result.Rejection);
            Assert.AreEqual(1, system.ActiveAbilityCount);
        }

        [Test]
        public void AbilitiesInDifferentGroupsRunTogether()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition attack = NewAbility("test.group.attack");
            attack.SetExclusionGroupForTests(
                Attacks, AbilityGroupExclusionPolicy.BlockWhileSameGroupActive);
            TimelineAbilityDefinition aura = NewAbility("test.group.aura");
            aura.SetExclusionGroupForTests(
                Auras, AbilityGroupExclusionPolicy.BlockWhileSameGroupActive);
            Grant(system, attack, aura);

            Assert.IsTrue(system.TryActivate(attack, AbilityContext.FromTarget(owner, null)));
            Assert.IsTrue(system.TryActivate(aura, AbilityContext.FromTarget(owner, null)));

            Assert.AreEqual(2, system.ActiveAbilityCount);
            Assert.AreEqual(
                attack,
                system.ActiveAbility,
                "the ability that started first keeps the singular accessors");
        }

        [Test]
        public void AnAbilityInTheSameGroupIsBlocked()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition first = NewAbility("test.group.same.first");
            first.SetExclusionGroupForTests(
                Attacks, AbilityGroupExclusionPolicy.BlockWhileSameGroupActive);
            TimelineAbilityDefinition second = NewAbility("test.group.same.second");
            second.SetExclusionGroupForTests(
                Attacks, AbilityGroupExclusionPolicy.BlockWhileSameGroupActive);
            Grant(system, first, second);

            Assert.IsTrue(system.TryActivate(first, AbilityContext.FromTarget(owner, null)));
            Assert.IsFalse(
                system.TryActivate(
                    second,
                    AbilityContext.FromTarget(owner, null),
                    out AbilityActivationResult result));

            Assert.AreEqual(AbilityActivationRejection.BlockedByGroup, result.Rejection);
        }

        [Test]
        public void CancelSameGroup_TakesTheGroupAndLeavesTheRestRunning()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition aura = NewAbility("test.group.cancel.aura");
            aura.SetExclusionGroupForTests(
                Auras, AbilityGroupExclusionPolicy.BlockWhileSameGroupActive);
            TimelineAbilityDefinition first = NewAbility("test.group.cancel.first");
            first.SetExclusionGroupForTests(
                Attacks, AbilityGroupExclusionPolicy.BlockWhileSameGroupActive);
            TimelineAbilityDefinition second = NewAbility("test.group.cancel.second");
            second.SetExclusionGroupForTests(
                Attacks, AbilityGroupExclusionPolicy.CancelSameGroup);
            Grant(system, aura, first, second);

            Assert.IsTrue(system.TryActivate(aura, AbilityContext.FromTarget(owner, null)));
            Assert.IsTrue(system.TryActivate(first, AbilityContext.FromTarget(owner, null)));

            GameplayTag cancelledWith = default;
            system.AbilityCancelled += (_, tag) => cancelledWith = tag;

            Assert.IsTrue(system.TryActivate(second, AbilityContext.FromTarget(owner, null)));

            Assert.AreEqual(CommonGameplayTags.CancelSuperseded, cancelledWith);
            Assert.AreEqual(2, system.ActiveAbilityCount, "the aura was never in the way");
            Assert.AreEqual(aura, system.ActiveAbility);
            CollectionAssert.DoesNotContain(RunningAbilities(system), first);
            CollectionAssert.Contains(RunningAbilities(system), second);
        }

        [Test]
        public void CancelAnyActive_IsRefusedByAnAbilityThatCannotBeCancelled()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition stubborn = NewAbility("test.group.stubborn");
            stubborn.SetCancellationForTests(AbilityCancelPolicy.Nothing);
            TimelineAbilityDefinition usurper = NewAbility("test.group.usurper");
            usurper.SetExclusionGroupForTests(
                default, AbilityGroupExclusionPolicy.CancelAnyActive);
            Grant(system, stubborn, usurper);

            Assert.IsTrue(system.TryActivate(stubborn, AbilityContext.FromTarget(owner, null)));
            Assert.IsFalse(
                system.TryActivate(
                    usurper,
                    AbilityContext.FromTarget(owner, null),
                    out AbilityActivationResult result));

            Assert.AreEqual(
                AbilityActivationRejection.BlockedByUncancellableAbility, result.Rejection);
            Assert.AreEqual(stubborn, system.ActiveAbility, "and nothing was cancelled");
        }

        [Test]
        public void EvaluatingAnActivationThatWouldCancel_ChangesNothing()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition first = NewAbility("test.group.sideeffect.first");
            TimelineAbilityDefinition second = NewAbility("test.group.sideeffect.second");
            second.SetExclusionGroupForTests(
                default, AbilityGroupExclusionPolicy.CancelAnyActive);
            Grant(system, first, second);

            Assert.IsTrue(system.TryActivate(first, AbilityContext.FromTarget(owner, null)));

            AbilityActivationResult evaluated =
                system.EvaluateActivation(second, AbilityContext.FromTarget(owner, null));

            Assert.IsTrue(evaluated.IsAccepted);
            Assert.AreEqual(first, system.ActiveAbility, "CanActivate stays side-effect free");
        }

        [Test]
        public void ConcurrentAbilitiesEachRunTheirOwnStepTimeline()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition combo = NewAbility("test.group.concurrent.combo", 2);
            combo.SetStepAdvancementForTests(AbilityStepAdvancement.Automatic);
            combo.SetExclusionGroupForTests(
                Attacks, AbilityGroupExclusionPolicy.BlockWhileSameGroupActive);
            TimelineAbilityDefinition aura = NewAbility("test.group.concurrent.aura");
            aura.SetExclusionGroupForTests(
                Auras, AbilityGroupExclusionPolicy.BlockWhileSameGroupActive);
            Grant(system, combo, aura);

            Assert.IsTrue(system.TryActivate(combo, AbilityContext.FromTarget(owner, null)));
            Assert.IsTrue(system.TryActivate(aura, AbilityContext.FromTarget(owner, null)));

            for (int i = 0; i < 12 && system.ActiveAbilityCount == 2; i++)
            {
                system.Tick(Tick);
            }

            Assert.AreEqual(
                1,
                system.ActiveAbilityCount,
                "the one-step aura ended while the two-step combo carried on");
            Assert.AreEqual(combo, system.ActiveAbility);
            Assert.AreEqual(1, system.ActiveStepIndex);
        }

        [Test]
        public void CancellingCancelsEveryRunningAbility()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition attack = NewAbility("test.group.cancelall.attack");
            attack.SetExclusionGroupForTests(
                Attacks, AbilityGroupExclusionPolicy.BlockWhileSameGroupActive);
            TimelineAbilityDefinition aura = NewAbility("test.group.cancelall.aura");
            aura.SetExclusionGroupForTests(
                Auras, AbilityGroupExclusionPolicy.BlockWhileSameGroupActive);
            Grant(system, attack, aura);

            Assert.IsTrue(system.TryActivate(attack, AbilityContext.FromTarget(owner, null)));
            Assert.IsTrue(system.TryActivate(aura, AbilityContext.FromTarget(owner, null)));

            Assert.IsTrue(system.TryCancelActiveAbility(CommonGameplayTags.CancelManual));

            Assert.AreEqual(0, system.ActiveAbilityCount);
            Assert.IsFalse(system.IsActive);
        }

        [Test]
        public void CancellingOneAbilityLeavesTheOthersAlone()
        {
            AbilitySystem system = NewSystem(out GameObject owner);
            TimelineAbilityDefinition attack = NewAbility("test.group.cancelone.attack");
            attack.SetExclusionGroupForTests(
                Attacks, AbilityGroupExclusionPolicy.BlockWhileSameGroupActive);
            TimelineAbilityDefinition aura = NewAbility("test.group.cancelone.aura");
            aura.SetExclusionGroupForTests(
                Auras, AbilityGroupExclusionPolicy.BlockWhileSameGroupActive);
            Grant(system, attack, aura);

            Assert.IsTrue(system.TryActivate(attack, AbilityContext.FromTarget(owner, null)));
            Assert.IsTrue(system.TryActivate(aura, AbilityContext.FromTarget(owner, null)));

            Assert.IsTrue(system.TryCancelAbility(aura, CommonGameplayTags.CancelManual));

            Assert.AreEqual(1, system.ActiveAbilityCount);
            Assert.AreEqual(attack, system.ActiveAbility);
        }

        [Test]
        public void AGroupScopedPolicyWithoutAGroupFailsValidation()
        {
            TimelineAbilityDefinition ability = NewAbility("test.group.novalidgroup");
            ability.SetExclusionGroupForTests(
                default, AbilityGroupExclusionPolicy.BlockWhileSameGroupActive);

            Assert.IsFalse(ability.TryValidate(out string error));
            StringAssert.Contains("Group Tag", error);
        }

        // ------------------------------------------------------------ helpers

        private static List<AbilityDefinition> RunningAbilities(AbilitySystem system)
        {
            var abilities = new List<AbilityDefinition>();
            foreach (AbilityInstance instance in system.ActiveInstances)
            {
                abilities.Add(instance.Definition);
            }

            return abilities;
        }

        private AbilitySystem NewSystem(out GameObject owner)
        {
            owner = new GameObject("AbilitySystemOwner");
            owned.Add(owner);
            return owner.AddComponent<AbilitySystem>();
        }

        private TimelineAbilityDefinition NewAbility(string abilityId, int stepCount = 1)
        {
            TimelineAbilityDefinition ability = ScriptableObject.CreateInstance<TimelineAbilityDefinition>();
            owned.Add(ability);
            ability.SetAbilityIdForTests(abilityId);
            SetField(ability, "requiresTarget", false);

            var steps = new AbilityStep[stepCount];
            for (int i = 0; i < stepCount; i++)
            {
                steps[i] = new AbilityStep();
                steps[i].ConfigureForTests(1, 2, 10, 60f);
            }

            ability.SetStepsForTests(steps);
            return ability;
        }

        private void Grant(AbilitySystem system, params AbilityDefinition[] abilities)
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
            // now sit one level above the timeline type most fixtures use.
            for (System.Type type = target.GetType(); type != null; type = type.BaseType)
            {
                FieldInfo field = type.GetField(
                    fieldName,
                    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
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
