using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    /// <summary>
    /// The cue lifecycles and what surrounds them: an effect drives Execute,
    /// Add, WhileActive and Remove under one handle; a refused application
    /// raises the replacement the author wrote for that outcome; a presenter
    /// arriving late is caught up without a replayed burst; filters and
    /// per-frame batching; and the shape of the parameters, which hands a
    /// presenter nothing it could change.
    /// </summary>
    public sealed class GameplayCueLifecycleTests
    {
        private static readonly GameplayAttribute Health = new("Test.Health");
        private static readonly GameplayTag HitCue = new("Cue.Test.Hit");
        private static readonly GameplayTag BlockedCue = new("Cue.Test.Blocked");
        private static readonly GameplayTag ImmuneCue = new("Cue.Test.Immune");
        private static readonly GameplayTag ParriedCue = new("Cue.Test.Parried");
        private static readonly GameplayTag BurnTag = new("Effect.Test.Burn");

        private readonly List<Object> owned = new();

        private GameObject actor;
        private AbilitySystem system;
        private GameplayEffectContainer effects;
        private GameObject attacker;
        private AbilitySystem attackerSystem;
        private RecordingPresenter presenter;

        [SetUp]
        public void SetUp()
        {
            actor = Own(new GameObject("CueActor"));
            actor.AddComponent<AttributeSet>().SetInitialValues(new[]
            {
                new AttributeSet.InitialValue(Health, 100f, 0f, 100f),
            });
            system = actor.AddComponent<AbilitySystem>();
            effects = GameplayEffectContainer.For(actor);
            attacker = Own(new GameObject("CueAttacker"));
            attackerSystem = attacker.AddComponent<AbilitySystem>();
            presenter = new RecordingPresenter();
            system.Cues.AddPresenter(presenter);
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
        }

        // ------------------------------------------------------- lifecycles

        [Test]
        public void AnInstantEffect_ExecutesItsCueOnTheTarget_WithMagnitudeAndSource()
        {
            GameplayEffectDefinition effect = NewInstant(-10f, HitCue);

            Assert.IsTrue(Apply(effect).Succeeded);

            Assert.AreEqual(1, presenter.Received.Count);
            GameplayCueParameters cue = presenter.Received[0];
            Assert.AreEqual(GameplayCueEvent.Execute, cue.Event);
            Assert.AreEqual(GameplayCueOutcome.Landed, cue.Outcome);
            Assert.AreEqual(HitCue, cue.Cue);
            Assert.AreSame(actor, cue.Owner);
            Assert.AreSame(attacker, cue.Source);
            Assert.AreSame(effect, cue.Effect);
            Assert.AreEqual(10f, cue.Magnitude, 1e-4f);
            Assert.IsTrue(cue.Handle.IsValid);
            Assert.IsFalse(cue.IsPersistent);
            Assert.AreEqual(0, system.Cues.ActiveCues.Count, "a burst is not kept");
        }

        [Test]
        public void APersistentEffect_AddsItsCue_AndRemovingTheEffectRemovesIt()
        {
            GameplayEffectDefinition effect = NewDuration(5f, HitCue);

            GameplayEffectApplicationResult result = Apply(effect);

            Assert.AreEqual(GameplayCueEvent.Add, presenter.Received[0].Event);
            GameplayCueHandle handle = presenter.Received[0].Handle;
            Assert.IsTrue(handle.IsValid);
            Assert.AreEqual(1, system.Cues.ActiveCues.Count);
            Assert.IsTrue(effects.TryGetActiveEffect(result.Handle, out ActiveGameplayEffect active));
            Assert.AreEqual(handle, active.CueHandle, "the effect remembers the cue it added");

            Assert.IsTrue(effects.TryRemove(result.Handle));

            Assert.AreEqual(GameplayCueEvent.Remove, presenter.Received[^1].Event);
            Assert.AreEqual(handle, presenter.Received[^1].Handle, "same handle from Add to Remove");
            Assert.AreEqual(0, system.Cues.ActiveCues.Count);
        }

        [Test]
        public void AnExpiringEffect_RemovesItsCue()
        {
            Apply(NewDuration(1f, HitCue));
            Assert.AreEqual(1, system.Cues.ActiveCues.Count);

            effects.Tick(1.5f);

            Assert.AreEqual(GameplayCueEvent.Remove, presenter.Received[^1].Event);
            Assert.AreEqual(0, system.Cues.ActiveCues.Count);
        }

        [Test]
        public void Stacking_RefreshesTheCue_WithTheStackCount_UnderTheSameHandle()
        {
            GameplayEffectDefinition effect = NewDuration(5f, HitCue);
            effect.SetStackingForTests(GameplayEffectStacking.With(EffectStacking.Stack));

            Apply(effect);
            GameplayCueHandle handle = presenter.Received[0].Handle;
            Apply(effect);

            GameplayCueParameters refreshed = presenter.Received[^1];
            Assert.AreEqual(GameplayCueEvent.WhileActive, refreshed.Event);
            Assert.AreEqual(handle, refreshed.Handle);
            Assert.AreEqual(2, refreshed.StackCount);
            Assert.AreEqual(1, system.Cues.ActiveCues.Count, "one cue, two stacks");
            Assert.AreEqual(2, system.Cues.ActiveCues[0].Parameters.StackCount);
        }

        /// <summary>
        /// The second acceptance criterion: a presenter that arrives while a
        /// persistent cue is live gets WhileActive for it, and never the burst
        /// that fired before it existed.
        /// </summary>
        [Test]
        public void ALatePresenter_IsCaughtUpWithWhileActive_AndNoBursts()
        {
            Apply(NewInstant(-10f, new GameplayTag("Cue.Test.Burst")));
            Apply(NewDuration(5f, HitCue));
            RecordingPresenter late = new();

            system.Cues.AddPresenter(late);

            Assert.AreEqual(1, late.Received.Count);
            Assert.AreEqual(GameplayCueEvent.WhileActive, late.Received[0].Event);
            Assert.AreEqual(HitCue, late.Received[0].Cue);
            Assert.AreEqual(system.Cues.ActiveCues[0].Handle, late.Received[0].Handle);
            Assert.AreEqual(0, late.Count(GameplayCueEvent.Execute));
            Assert.AreEqual(0, late.Count(GameplayCueEvent.Add));
        }

        [Test]
        public void RemoveAll_EndsEveryPersistentCue()
        {
            Apply(NewDuration(5f, HitCue));
            Apply(NewDuration(5f, new GameplayTag("Cue.Test.Other")));

            Assert.AreEqual(2, system.Cues.RemoveAll());

            Assert.AreEqual(2, presenter.Count(GameplayCueEvent.Remove));
            Assert.AreEqual(0, system.Cues.ActiveCues.Count);
        }

        /// <summary>
        /// The actor going away ends its persistent cues, so a pooled loop is
        /// returned. Edit Mode never dispatches the lifecycle messages, so the
        /// test sends the one the runtime would.
        /// </summary>
        [Test]
        public void TheOwnerTearingDown_RemovesItsPersistentCues()
        {
            Apply(NewDuration(5f, HitCue));
            Assert.AreEqual(1, system.Cues.ActiveCues.Count);

            typeof(AbilitySystem)
                .GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(system, null);

            Assert.AreEqual(1, presenter.Count(GameplayCueEvent.Remove));
            Assert.AreEqual(0, system.Cues.ActiveCues.Count);
        }

        // --------------------------------------------------------- outcomes

        [Test]
        public void ARefusedApplication_RaisesTheReplacementForItsOutcome_OrNothing()
        {
            GameplayEffectDefinition effect = NewInstant(
                -10f, HitCue, new GameplayCueReplacement(GameplayCueOutcome.Blocked, BlockedCue));
            effect.SetTagsForTests(blocked: new[] { CommonGameplayTags.Blocking });
            system.SetLooseTag(CommonGameplayTags.Blocking, true);

            Assert.IsFalse(Apply(effect).Succeeded);

            Assert.AreEqual(1, presenter.Received.Count);
            Assert.AreEqual(BlockedCue, presenter.Received[0].Cue);
            Assert.AreEqual(GameplayCueOutcome.Blocked, presenter.Received[0].Outcome);
            Assert.AreEqual(100f, HealthOf(actor), 1e-3f);

            GameplayEffectDefinition silent = NewInstant(-10f, HitCue);
            silent.SetTagsForTests(blocked: new[] { CommonGameplayTags.Blocking });
            Assert.IsFalse(Apply(silent).Succeeded);
            Assert.AreEqual(1, presenter.Received.Count, "no replacement, no cue");
        }

        [Test]
        public void AnImmuneTarget_RaisesTheImmuneReplacement()
        {
            GameplayEffectDefinition ward = NewDuration(5f, default);
            ward.SetTagsForTests(immunity: new[] { BurnTag });
            Assert.IsTrue(Apply(ward).Succeeded);
            GameplayEffectDefinition burn = NewInstant(
                -10f, HitCue, new GameplayCueReplacement(GameplayCueOutcome.Immune, ImmuneCue));
            burn.SetTagsForTests(effect: new[] { BurnTag });

            Assert.IsFalse(Apply(burn).Succeeded);

            Assert.AreEqual(ImmuneCue, presenter.Received[^1].Cue);
            Assert.AreEqual(GameplayCueOutcome.Immune, presenter.Received[^1].Outcome);
        }

        [Test]
        public void AParriedDamage_RaisesTheParriedReplacement_WithTheAttackerAsSource()
        {
            DamageEffectDefinition damage = Own(ScriptableObject.CreateInstance<DamageEffectDefinition>());
            damage.name = "GE_Damage_CueTest";
            damage.SetTargetingForTests(GameplayEffectTargeting.AbilityTarget);
            damage.ConfigureDamageForTests(10f, Health);
            damage.SetCueForTests(
                HitCue, new GameplayCueReplacement(GameplayCueOutcome.Parried, ParriedCue));
            system.SetLooseTag(CommonGameplayTags.Parrying, true);

            Assert.IsFalse(Apply(damage).Succeeded);

            GameplayCueParameters cue = presenter.Received[^1];
            Assert.AreEqual(ParriedCue, cue.Cue);
            Assert.AreEqual(GameplayCueOutcome.Parried, cue.Outcome);
            Assert.AreSame(attacker, cue.Source);
            Assert.AreEqual(100f, HealthOf(actor), 1e-3f);

            system.SetLooseTag(CommonGameplayTags.Parrying, false);
            Assert.IsTrue(Apply(damage).Succeeded);
            Assert.AreEqual(HitCue, presenter.Received[^1].Cue);
            Assert.AreEqual(10f, presenter.Received[^1].Magnitude, 1e-4f, "damage is the magnitude");
        }

        // ------------------------------------------------------- step cues

        [Test]
        public void AStepCue_FiresOnceAtItsFrame_FromTheAbility()
        {
            TimelineAbilityDefinition ability = NewAbility("test.cue.step", cueFrame: 2);
            Grant(ability);
            Assert.IsTrue(system.TryActivate(ability, AbilityContext.FromTarget(actor, null)));

            // Half a frame in, the step is still on frame 1; the cue waits for 2.
            system.Tick(0.5f / 60f);
            Assert.AreEqual(0, presenter.Received.Count, "frame 1 is before the cue");
            system.Tick(1f / 60f);
            system.Tick(1f / 60f);

            Assert.AreEqual(1, presenter.Received.Count);
            GameplayCueParameters cue = presenter.Received[0];
            Assert.AreSame(ability, cue.Ability);
            Assert.AreSame(actor, cue.Owner);
            Assert.AreEqual(GameplayCueOutcome.Landed, cue.Outcome, "a step with no effects has nothing to miss");
        }

        [Test]
        public void AStepCueOnAMissedSwing_CarriesTheMissedOutcome_OrIsSuppressed()
        {
            TimelineAbilityDefinition swing = NewSwingWithDamage("test.cue.miss", suppressOnMiss: false);
            Grant(swing);
            Assert.IsTrue(system.TryActivate(swing, AbilityContext.FromTarget(actor, null)));
            RunFrames(4);

            Assert.AreEqual(1, presenter.Received.Count);
            Assert.AreEqual(GameplayCueOutcome.Missed, presenter.Received[0].Outcome);

            presenter.Received.Clear();
            system.ForceCancelActiveAbility(CommonGameplayTags.CancelManual);
            TimelineAbilityDefinition quiet = NewSwingWithDamage("test.cue.quiet", suppressOnMiss: true);
            Grant(quiet);
            Assert.IsTrue(system.TryActivate(quiet, AbilityContext.FromTarget(actor, null)));
            RunFrames(4);

            Assert.AreEqual(0, presenter.Received.Count, "suppressed on a miss");
        }

        [Test]
        public void AStepCueOnALandedSwing_CarriesTheLandedOutcome()
        {
            GameObject victim = Own(new GameObject("CueVictim"));
            victim.transform.position = actor.transform.position + Vector3.forward * 1.5f;
            victim.AddComponent<SphereCollider>().radius = 0.5f;
            victim.AddComponent<AttributeSet>().SetInitialValues(new[]
            {
                new AttributeSet.InitialValue(Health, 100f, 0f, 100f),
            });
            TimelineAbilityDefinition swing = NewSwingWithDamage("test.cue.land", suppressOnMiss: true);
            Grant(swing);
            Physics.SyncTransforms();
            Assert.IsTrue(system.TryActivate(swing, AbilityContext.FromDirection(actor, null, Vector3.forward)));

            RunFrames(4);

            Assert.AreEqual(90f, HealthOf(victim), 1e-3f, "the swing landed");
            Assert.AreEqual(1, presenter.Received.Count);
            Assert.AreEqual(GameplayCueOutcome.Landed, presenter.Received[0].Outcome);
        }

        [Test]
        public void AManualCue_GoesThroughTheDispatcher()
        {
            system.TriggerGameplayCue(HitCue, AbilityContext.FromTarget(actor, attacker));

            Assert.AreEqual(1, presenter.Received.Count);
            Assert.AreEqual(GameplayCueEvent.Execute, presenter.Received[0].Event);
            Assert.AreSame(attacker, presenter.Received[0].Context.Target);
        }

        // ------------------------------------------------ filters, batching

        [Test]
        public void AFilter_CanReplaceOrSuppressACue()
        {
            ReplacingFilter filter = new() { From = HitCue, To = BlockedCue };
            system.Cues.AddFilter(filter);

            Apply(NewInstant(-10f, HitCue));
            Assert.AreEqual(BlockedCue, presenter.Received[^1].Cue);

            filter.Suppress = true;
            int before = presenter.Received.Count;
            Assert.IsFalse(system.Cues.Execute(
                GameplayCueParameters.ForContext(HitCue, AbilityContext.FromTarget(actor, null))));
            Assert.AreEqual(before, presenter.Received.Count);

            system.Cues.RemoveFilter(filter);
            Apply(NewInstant(-10f, HitCue));
            Assert.AreEqual(HitCue, presenter.Received[^1].Cue);
        }

        [Test]
        public void DuplicateBursts_AreBatchedWithinOneFrame()
        {
            GameplayCueParameters burst = GameplayCueParameters.ForContext(
                HitCue, AbilityContext.FromTarget(actor, null));

            Assert.IsTrue(system.Cues.Execute(in burst));
            Assert.IsFalse(system.Cues.Execute(in burst));
            Assert.AreEqual(1, presenter.Received.Count);
            Assert.AreEqual(1, system.Cues.BatchedCount);

            system.Cues.BatchDuplicatesPerFrame = false;
            Assert.IsTrue(system.Cues.Execute(in burst));
            Assert.AreEqual(2, presenter.Received.Count);
        }

        [Test]
        public void TwoEffectsWithOneCue_OnOneHit_RaiseItOnce()
        {
            Apply(NewInstant(-10f, HitCue));
            Apply(NewInstant(-5f, HitCue));

            Assert.AreEqual(1, presenter.Count(GameplayCueEvent.Execute));
            Assert.AreEqual(85f, HealthOf(actor), 1e-3f, "both effects still landed");
        }

        [Test]
        public void AThrowingPresenter_IsLoggedAndSkipped_AndTheRestStillPresent()
        {
            RecordingPresenter after = new();
            system.Cues.AddPresenter(new ThrowingPresenter());
            system.Cues.AddPresenter(after);
            LogAssert.Expect(LogType.Exception, new Regex("cosmetic"));

            Assert.IsTrue(Apply(NewInstant(-10f, HitCue)).Succeeded);

            Assert.AreEqual(1, after.Received.Count);
            Assert.AreEqual(90f, HealthOf(actor), 1e-3f, "the application was never in question");
        }

        // ------------------------------------------------------ the shape

        /// <summary>
        /// The third acceptance criterion, pinned on the type: a presenter is
        /// handed values, definitions and actors — never the activation, the
        /// spec, the system or the container it could change.
        /// </summary>
        [Test]
        public void CueParameters_HandAPresenterNothingItCouldChange()
        {
            System.Type[] forbidden =
            {
                typeof(AbilityInstance), typeof(GameplayEffectSpec), typeof(AbilitySystem),
                typeof(GameplayEffectContainer), typeof(ActiveGameplayEffect), typeof(AbilityTargetData),
            };

            Assert.IsEmpty(
                typeof(GameplayCueParameters).GetFields(BindingFlags.Public | BindingFlags.Instance),
                "no public fields");
            foreach (PropertyInfo property in typeof(GameplayCueParameters).GetProperties())
            {
                Assert.IsFalse(property.CanWrite, $"{property.Name} must be read-only");
                CollectionAssert.DoesNotContain(forbidden, property.PropertyType, property.Name);
            }

            MethodInfo[] methods = typeof(IGameplayCuePresenter).GetMethods();
            Assert.AreEqual(1, methods.Length);
            Assert.AreEqual(typeof(void), methods[0].ReturnType, "a presenter answers nothing");
        }

        [Test]
        public void AnEffectWithTwoReplacementsForOneOutcome_FailsValidation()
        {
            GameplayEffectDefinition effect = NewInstant(
                -10f,
                HitCue,
                new GameplayCueReplacement(GameplayCueOutcome.Blocked, BlockedCue),
                new GameplayCueReplacement(GameplayCueOutcome.Blocked, ImmuneCue));

            Assert.IsFalse(effect.TryValidate(out string error));
            StringAssert.Contains("Cue replacements", error);
        }

        // ------------------------------------------------------------ helpers

        private static float HealthOf(GameObject target)
        {
            return target.GetComponent<AttributeSet>().GetCurrent(Health);
        }

        private GameplayEffectApplicationResult Apply(GameplayEffectDefinition effect)
        {
            return effects.Apply(new GameplayEffectSpec(effect, attacker, actor));
        }

        private void RunFrames(int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                system.Tick(1f / 60f);
            }
        }

        private GameplayEffectDefinition NewInstant(
            float healthDelta, GameplayTag cue, params GameplayCueReplacement[] replacements)
        {
            GameplayEffectDefinition effect = Own(ScriptableObject.CreateInstance<GameplayEffectDefinition>());
            effect.name = "GE_Test_Cue";
            effect.ConfigureForTests(
                GameplayEffectDurationPolicy.Instant,
                GameplayEffectTargeting.AbilityTarget,
                new[] { new GameplayEffectModifier(Health, AttributeOperation.Add, healthDelta) });
            effect.SetCueForTests(cue, replacements);
            return effect;
        }

        private GameplayEffectDefinition NewDuration(float seconds, GameplayTag cue)
        {
            GameplayEffectDefinition effect = Own(ScriptableObject.CreateInstance<GameplayEffectDefinition>());
            effect.name = "GE_Test_CueDuration";
            effect.ConfigureForTests(
                GameplayEffectDurationPolicy.Duration,
                GameplayEffectTargeting.AbilityTarget,
                new[] { new GameplayEffectModifier(Health, AttributeOperation.Add, -1f) },
                durationSeconds: seconds);
            effect.SetCueForTests(cue);
            return effect;
        }

        private TimelineAbilityDefinition NewAbility(string id, int cueFrame)
        {
            TimelineAbilityDefinition ability = Own(ScriptableObject.CreateInstance<TimelineAbilityDefinition>());
            ability.SetAbilityIdForTests(id);
            SetField(ability, "requiresTarget", false);
            AbilityStep step = new();
            step.ConfigureForTests(1, 2, 30, 60f);
            step.SetCueTriggersForTests(new[] { new GameplayCueTrigger(cueFrame, HitCue) });
            ability.SetStepsForTests(step);
            return ability;
        }

        /// <summary>A swing whose damage fires on frame 1 and whose cue fires on frame 2.</summary>
        private TimelineAbilityDefinition NewSwingWithDamage(string id, bool suppressOnMiss)
        {
            DamageEffectDefinition damage = Own(ScriptableObject.CreateInstance<DamageEffectDefinition>());
            damage.name = "GE_Damage_CueSwing";
            damage.SetTargetingForTests(GameplayEffectTargeting.FromShape(
                HitShape.Sphere(new Vector3(0f, 0f, 1.5f), 1f)
                    .WithOrigin(HitShapeOrigin.OwnerLocal)
                    .WithLayers(Physics.AllLayers),
                8,
                false));
            damage.ConfigureDamageForTests(10f, Health);
            damage.SetReactionEventTagForTests(default);

            TimelineAbilityDefinition swing = Own(ScriptableObject.CreateInstance<TimelineAbilityDefinition>());
            swing.SetAbilityIdForTests(id);
            SetField(swing, "requiresTarget", false);
            AbilityStep step = new();
            step.ConfigureForTests(1, 2, 30, 60f);
            step.SetEffectTriggersForTests(new[] { new AbilityEffectTrigger(1, damage) });
            step.SetCueTriggersForTests(new[] { new GameplayCueTrigger(2, HitCue, suppressOnMiss) });
            swing.SetStepsForTests(step);
            return swing;
        }

        private void Grant(params AbilityDefinition[] abilities)
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

        private sealed class RecordingPresenter : IGameplayCuePresenter
        {
            public readonly List<GameplayCueParameters> Received = new();

            public void OnGameplayCue(in GameplayCueParameters parameters)
            {
                Received.Add(parameters);
            }

            public int Count(GameplayCueEvent cueEvent)
            {
                int count = 0;
                for (int i = 0; i < Received.Count; i++)
                {
                    if (Received[i].Event == cueEvent)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        private sealed class ThrowingPresenter : IGameplayCuePresenter
        {
            public void OnGameplayCue(in GameplayCueParameters parameters)
            {
                throw new System.InvalidOperationException("cosmetic code broke");
            }
        }

        private sealed class ReplacingFilter : IGameplayCueFilter
        {
            public GameplayTag From;
            public GameplayTag To;
            public bool Suppress;

            public bool Filter(ref GameplayCueParameters parameters)
            {
                if (parameters.Cue != From)
                {
                    return true;
                }

                if (Suppress)
                {
                    return false;
                }

                parameters = parameters.WithCue(To);
                return true;
            }
        }
    }
}
