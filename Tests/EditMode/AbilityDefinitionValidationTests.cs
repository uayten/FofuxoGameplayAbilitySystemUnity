using NUnit.Framework;
using UnityEngine;
using Fofuxo.GameplayAbilitySystem;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    public sealed class AbilityDefinitionValidationTests
    {
        [Test]
        public void FreshAbility_HasNoId_AndFailsValidation()
        {
            AbilityDefinition ability = ScriptableObject.CreateInstance<AbilityDefinition>();
            try
            {
                Assert.IsTrue(string.IsNullOrWhiteSpace(ability.AbilityId));
                Assert.IsFalse(ability.TryValidate(out string error));
                Assert.IsFalse(string.IsNullOrWhiteSpace(error));
            }
            finally
            {
                Object.DestroyImmediate(ability);
            }
        }

        [Test]
        public void DefaultFrames_MapToStartupActiveRecovery()
        {
            AbilityDefinition ability = ScriptableObject.CreateInstance<AbilityDefinition>();
            try
            {
                Assert.AreEqual(AbilityPhase.Startup, ability.GetPhase(1));
                Assert.AreEqual(AbilityPhase.Active, ability.GetPhase(2));
                Assert.AreEqual(AbilityPhase.Recovery, ability.GetPhase(ability.RecoveryEndFrame));
            }
            finally
            {
                Object.DestroyImmediate(ability);
            }
        }

        [Test]
        public void NullParryEffect_FailsValidation()
        {
            AbilityDefinition ability = ScriptableObject.CreateInstance<AbilityDefinition>();
            try
            {
                ability.SetAbilityIdForTests("test.parry");
                ability.SetParryEffectsForTests(null, null);
                Assert.IsFalse(ability.TryValidate(out string error));
                Assert.IsTrue(error.Contains("Parry"));
            }
            finally
            {
                Object.DestroyImmediate(ability);
            }
        }

        [Test]
        public void DefaultCancelMask_AllowsEveryReason()
        {
            AbilityDefinition ability = ScriptableObject.CreateInstance<AbilityDefinition>();
            try
            {
                foreach (AbilityCancelReason reason in System.Enum.GetValues(typeof(AbilityCancelReason)))
                {
                    Assert.IsTrue(ability.CanBeCancelledBy(reason), reason.ToString());
                }
            }
            finally
            {
                Object.DestroyImmediate(ability);
            }
        }
    }
}
