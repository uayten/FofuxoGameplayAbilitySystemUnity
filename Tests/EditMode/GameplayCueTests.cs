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
            TimelineAbilityDefinition ability = ScriptableObject.CreateInstance<TimelineAbilityDefinition>();
            try
            {
                SetField(ability, "abilityId", "test.cue.empty");
                ability.FirstStepForTests.SetCueTriggersForTests(new GameplayCueTrigger[1]);
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
                system.GameplayCueTriggered += cue =>
                {
                    invokeCount++;
                    receivedCue = cue.Cue;
                    Assert.AreEqual(GameplayCueEvent.Execute, cue.Event);
                    Assert.AreSame(owner, cue.Owner);
                    Assert.AreSame(owner, cue.Context.Owner);
                    Assert.AreSame(target, cue.Context.Target);
                    Assert.AreEqual(target.transform.position, cue.Location);
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
