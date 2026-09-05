using NUnit.Framework;
using UnityEngine;
using Fofuxo.GameplayAbilitySystem;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    public sealed class GameplayCueTests
    {
        [Test]
        public void TryValidate_RejectsCueTriggerWithoutTag()
        {
            AbilityDefinition ability = ScriptableObject.CreateInstance<AbilityDefinition>();
            try
            {
                SetField(ability, "abilityId", "test.cue.empty");
                SetField(ability, "cueTriggers", new GameplayCueTrigger[1]);
                Assert.IsFalse(ability.TryValidate(out string error));
                Assert.IsTrue(error.Contains("cue"), error);
            }
            finally
            {
                Object.DestroyImmediate(ability);
            }
        }

        [Test]
        public void TriggerGameplayCue_InvokesEventWithPayload_AndIgnoresEmptyTag()
        {
            GameObject owner = new("GameplayCueOwner");
            GameObject target = new("GameplayCueTarget");
            try
            {
                AbilitySystem system = owner.AddComponent<AbilitySystem>();
                int invokeCount = 0;
                GameplayTag receivedCue = default;
                system.GameplayCueTriggered += (_, cue, context) =>
                {
                    invokeCount++;
                    receivedCue = cue;
                    Assert.AreSame(owner, context.Owner);
                    Assert.AreSame(target, context.Target);
                };

                AbilityContext context = AbilityContext.FromTarget(owner, target);
                system.TriggerGameplayCue(new GameplayTag("Cue.Test"), context);
                system.TriggerGameplayCue(default, context);

                Assert.AreEqual(1, invokeCount);
                Assert.AreEqual(new GameplayTag("Cue.Test"), receivedCue);
            }
            finally
            {
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(owner);
            }
        }

        private static void SetField<TTarget, TValue>(TTarget target, string fieldName, TValue value)
        {
            var field = typeof(TTarget).GetField(
                fieldName,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(field, fieldName);
            field.SetValue(target, value);
        }
    }
}
