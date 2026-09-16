using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    /// <summary>
    /// The target-owned hit reaction, end to end: a damage effect lands on the
    /// target's attribute, asks the target to react, and the target's own
    /// ability owns the animation, the lock and the knockback from there. The
    /// three acceptance criteria of the milestone are pinned here — the target
    /// decides, cancelling the reaction cancels its knockback, and nothing hands
    /// reaction data to a consumer callback.
    /// </summary>
    public sealed class HitReactionTests
    {
        private const float FixedStep = 0.02f;
        private static readonly GameplayAttribute Health = new("Test.Health");
        private static readonly GameplayTag Stunned = CommonGameplayTags.Stunned;

        private readonly List<Object> owned = new();

        private GameObject attacker;
        private AbilitySystem attackerSystem;
        private GameObject target;
        private AbilitySystem targetSystem;
        private GameplayEffectContainer targetEffects;
        private RecordingMotor targetMotor;

        [SetUp]
        public void SetUp()
        {
            attacker = Own(new GameObject("HitAttacker"));
            attackerSystem = attacker.AddComponent<AbilitySystem>();

            target = Own(new GameObject("HitTarget"));
            target.transform.position = new Vector3(0f, 0f, 2f);
            target.AddComponent<SphereCollider>().radius = 0.5f;
            target.AddComponent<AttributeSet>().SetInitialValues(new[]
            {
                new AttributeSet.InitialValue(Health, 100f, 0f, 100f),
            });
            targetSystem = target.AddComponent<AbilitySystem>();
            targetMotor = new RecordingMotor();
            targetSystem.Motor = targetMotor;
            targetEffects = GameplayEffectContainer.For(target);
            Physics.SyncTransforms();
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

        // -------------------------------------------------------- the loop

        [Test]
        public void ALandedHit_DamagesTheAttribute_AndTheTargetsReactionOwnsTheKnockback()
        {
            HitReactionAbilityDefinition reaction = GrantReaction();
            DamageEffectDefinition effect = NewDamage(10f, horizontal: 6f, duration: 0.5f);

            Assert.AreEqual(1, Hit(effect));

            Assert.AreEqual(90f, HealthOf(target), 1e-3f);
            Assert.AreSame(reaction, targetSystem.ActiveAbility, "the target's reaction started");
            Assert.IsTrue(targetSystem.HasTag(Stunned), "and holds the lock it grants");

            AbilityInstance instance = targetSystem.ActiveInstances[0];
            Assert.AreSame(attacker, instance.TriggeringSpec.Source);
            Assert.AreSame(effect, instance.TriggeringSpec.Definition);

            ApplyKnockbackTask knockback = HitReactionAbilityDefinition.FindKnockback(instance);
            Assert.IsNotNull(knockback, "the reaction runs the push in its own scope");
            Assert.AreEqual(new Vector3(0f, 0f, 6f), knockback.Velocity);
            Assert.AreSame(attacker, knockback.Source);

            for (int i = 0; i < 40; i++)
            {
                targetSystem.TickFixed(FixedStep);
            }

            Assert.AreEqual(AbilityTaskState.Succeeded, knockback.State);
            Assert.AreEqual(3f, targetMotor.TotalDelta.magnitude, 1e-3f, "6 m/s for 0.5 s");
            Assert.Greater(targetMotor.TotalDelta.z, 0f, "pushed away from the attacker");
        }

        [Test]
        public void CancellingTheReaction_CancelsItsKnockbackWithIt()
        {
            GrantReaction();
            Hit(NewDamage(10f, horizontal: 6f, duration: 0.5f));
            ApplyKnockbackTask knockback =
                HitReactionAbilityDefinition.FindKnockback(targetSystem.ActiveInstances[0]);
            targetSystem.TickFixed(FixedStep);
            Vector3 travelled = targetMotor.TotalDelta;
            Assert.Greater(travelled.magnitude, 0f);

            targetSystem.ForceCancelActiveAbility(CommonGameplayTags.CancelManual);

            Assert.AreEqual(AbilityTaskState.Cancelled, knockback.State);
            for (int i = 0; i < 10; i++)
            {
                targetSystem.TickFixed(FixedStep);
            }

            Assert.AreEqual(travelled, targetMotor.TotalDelta, "not one more step of travel");
            Assert.IsFalse(targetSystem.HasTag(Stunned));
        }

        [Test]
        public void KnockbackScaleOfZero_TakesTheHitWithoutMoving()
        {
            HitReactionAbilityDefinition reaction = GrantReaction();
            SetField(reaction, "knockbackScale", 0f);

            Hit(NewDamage(10f, horizontal: 6f, duration: 0.5f));

            Assert.AreSame(reaction, targetSystem.ActiveAbility);
            Assert.IsNull(HitReactionAbilityDefinition.FindKnockback(targetSystem.ActiveInstances[0]));
        }

        [Test]
        public void AHitWithoutKnockback_StartsAReactionThatStands()
        {
            GrantReaction();

            Hit(NewDamage(10f));

            Assert.IsTrue(targetSystem.IsActive);
            Assert.IsNull(HitReactionAbilityDefinition.FindKnockback(targetSystem.ActiveInstances[0]));
        }

        [Test]
        public void ATargetWithoutAReactionAbility_StillTakesTheDamage()
        {
            Assert.AreEqual(1, Hit(NewDamage(10f, horizontal: 6f, duration: 0.5f)));

            Assert.AreEqual(90f, HealthOf(target), 1e-3f);
            Assert.IsFalse(targetSystem.IsActive);
        }

        [Test]
        public void AnEmptyReactionTag_DealsDamageAndAsksForNothing()
        {
            GrantReaction();
            DamageEffectDefinition effect = NewDamage(10f);
            effect.SetReactionEventTagForTests(default);

            Hit(effect);

            Assert.AreEqual(90f, HealthOf(target), 1e-3f);
            Assert.IsFalse(targetSystem.IsActive);
        }

        // ------------------------------------------------ the target decides

        [Test]
        public void AnInvulnerableTarget_TakesNoDamage_AndPlaysNoReaction()
        {
            GrantReaction();
            targetSystem.SetLooseTag(CommonGameplayTags.Invulnerable, true);
            int blocked = 0;
            targetEffects.EffectBlocked += _ => blocked++;

            Assert.AreEqual(0, Hit(NewDamage(10f, horizontal: 6f, duration: 0.5f)));

            Assert.AreEqual(100f, HealthOf(target), 1e-3f);
            Assert.IsFalse(targetSystem.IsActive);
            Assert.AreEqual(1, blocked, "the refusal is the target's, and it says so");
        }

        [Test]
        public void ADeadTarget_TakesNoDamage_AndPlaysNoReaction()
        {
            GrantReaction();
            targetSystem.SetLooseTag(CommonGameplayTags.Dead, true);

            Assert.AreEqual(0, Hit(NewDamage(10f)));

            Assert.AreEqual(100f, HealthOf(target), 1e-3f);
            Assert.IsFalse(targetSystem.IsActive);
        }

        /// <summary>
        /// A target that dies from the hit is dead by the time the reaction is
        /// requested — the attribute changes first, and the owner's death
        /// handler runs inside that change — so the reaction meets State.Dead.
        /// </summary>
        [Test]
        public void AHitThatKills_MeetsADeadTarget_WhenItAsksForTheReaction()
        {
            GrantReaction();
            target.GetComponent<AttributeSet>().Changed += change =>
            {
                if (change.Attribute == Health && change.NewValue <= 0f)
                {
                    targetSystem.SetLooseTag(CommonGameplayTags.Dead, true);
                }
            };

            Assert.AreEqual(1, Hit(NewDamage(100f)));

            Assert.AreEqual(0f, HealthOf(target), 1e-3f);
            Assert.IsFalse(targetSystem.IsActive, "the dead do not flinch");
        }

        [Test]
        public void AParryingTarget_RefusesAParryableHit_AndTakesAnUnparryableOne()
        {
            GrantReaction();
            targetSystem.SetLooseTag(CommonGameplayTags.Parrying, true);
            GameplayEffectSpec blockedSpec = null;
            targetEffects.EffectBlocked += spec => blockedSpec = spec;

            Assert.AreEqual(0, Hit(NewDamage(10f)));
            Assert.AreEqual(100f, HealthOf(target), 1e-3f);
            Assert.IsNotNull(blockedSpec, "the parry reward hangs on the blocked spec");
            Assert.AreSame(attacker, blockedSpec.Source);
            Assert.IsTrue(((DamageEffectDefinition)blockedSpec.Definition).CanBeParried);

            Assert.AreEqual(1, Hit(NewDamage(10f, parryable: false)));
            Assert.AreEqual(90f, HealthOf(target), 1e-3f);
            Assert.IsTrue(targetSystem.IsActive);
        }

        [Test]
        public void AnAuthoredBlockedTag_StillRefusesOnTopOfTheKindsRules()
        {
            GrantReaction();
            DamageEffectDefinition effect = NewDamage(10f);
            effect.SetTagsForTests(blocked: new[] { CommonGameplayTags.Blocking });
            targetSystem.SetLooseTag(CommonGameplayTags.Blocking, true);

            Assert.AreEqual(0, Hit(effect));
            Assert.AreEqual(100f, HealthOf(target), 1e-3f);
        }

        // --------------------------------------------------- repeats, order

        [Test]
        public void ASecondHit_RestartsTheReaction_WhenTheTriggerAsksForIt()
        {
            HitReactionAbilityDefinition reaction = GrantReaction(restartWhenActive: true);
            int started = 0;
            GameplayTag cancelled = default;
            targetSystem.AbilityStarted += _ => started++;
            targetSystem.AbilityCancelled += (_, tag) => cancelled = tag;

            Hit(NewDamage(10f, horizontal: 6f, duration: 0.5f));
            targetSystem.Tick(0.1f);
            int frameBefore = targetSystem.ActiveFrame;
            Hit(NewDamage(10f, horizontal: 6f, duration: 0.5f));

            Assert.AreEqual(2, started);
            Assert.AreEqual(CommonGameplayTags.CancelSuperseded, cancelled);
            Assert.AreSame(reaction, targetSystem.ActiveAbility);
            Assert.Less(targetSystem.ActiveFrame, frameBefore, "the timeline restarted");
            Assert.AreEqual(80f, HealthOf(target), 1e-3f);
        }

        [Test]
        public void ASecondHit_IsConsumedByTheRunningReaction_Otherwise()
        {
            GrantReaction(restartWhenActive: false);
            int started = 0;
            targetSystem.AbilityStarted += _ => started++;

            Hit(NewDamage(10f));
            targetSystem.Tick(0.1f);
            int frameBefore = targetSystem.ActiveFrame;
            Hit(NewDamage(10f));

            Assert.AreEqual(1, started);
            Assert.AreEqual(frameBefore, targetSystem.ActiveFrame);
            Assert.AreEqual(80f, HealthOf(target), 1e-3f, "the damage still lands");
        }

        /// <summary>
        /// Candidates are tried in loadout order and a refused one hands the
        /// event on: a guard reaction that requires State.Attacking sits above
        /// the plain flinch and takes the hit only while the owner swings.
        /// </summary>
        [Test]
        public void EventCandidates_FallThroughToTheNext_WhenTheFirstIsRefused()
        {
            HitReactionAbilityDefinition guard = NewReaction("test.reaction.guard");
            guard.SetRequiredTagsForTests(CommonGameplayTags.Attacking);
            HitReactionAbilityDefinition flinch = NewReaction("test.reaction.flinch");
            Grant(guard, flinch);

            Hit(NewDamage(10f));
            Assert.AreSame(flinch, targetSystem.ActiveAbility, "not attacking: the flinch answers");

            targetSystem.ForceCancelActiveAbility(CommonGameplayTags.CancelManual);
            targetSystem.SetLooseTag(CommonGameplayTags.Attacking, true);
            Hit(NewDamage(10f));
            Assert.AreSame(guard, targetSystem.ActiveAbility, "attacking: the guard answers first");
        }

        [Test]
        public void TheReactionInterruptsWhatTheTargetWasDoing()
        {
            GrantReaction();
            TimelineAbilityDefinition swing = NewSwing();
            Grant(swing, targetSystem.Loadout.Abilities[0]);
            Assert.IsTrue(targetSystem.TryActivate(swing, AbilityContext.FromTarget(target, null)));
            GameplayTag cancelled = default;
            targetSystem.AbilityCancelled += (_, tag) => cancelled = tag;

            Hit(NewDamage(10f));

            Assert.AreEqual(CommonGameplayTags.CancelSuperseded, cancelled);
            Assert.AreEqual(1, targetSystem.ActiveAbilityCount);
            Assert.IsInstanceOf<HitReactionAbilityDefinition>(targetSystem.ActiveAbility);
        }

        [Test]
        public void PeriodicDamage_TicksTheAttribute_AndAsksForTheReactionOnce()
        {
            GrantReaction();
            int started = 0;
            targetSystem.AbilityStarted += _ => started++;
            DamageEffectDefinition burn = NewDamage(10f);
            burn.ConfigureForTests(
                GameplayEffectDurationPolicy.Duration,
                burn.Targeting,
                null,
                durationSeconds: 1f,
                periodSeconds: 0.25f);
            // The application itself is the first tick; the periods that follow
            // are what this pins.
            burn.SetExecutePeriodOnApplicationForTests(false);

            Assert.AreEqual(1, Hit(burn));
            Assert.AreEqual(90f, HealthOf(target), 1e-3f);

            targetEffects.Tick(0.3f);

            Assert.AreEqual(80f, HealthOf(target), 1e-3f, "the tick dealt its damage");
            Assert.AreEqual(1, started, "and asked for no second flinch");
        }

        // -------------------------------------------------- the acceptance

        /// <summary>
        /// Nothing in the package hands reaction data to a consumer callback:
        /// the receiver contract and the struct it carried are gone, not renamed.
        /// </summary>
        [Test]
        public void NoTypeHandsReactionDataToAConsumerCallback()
        {
            System.Reflection.Assembly runtime = typeof(AbilitySystem).Assembly;

            Assert.IsNull(runtime.GetType("Fofuxo.GameplayAbilitySystem.IAbilityDamageReceiver"));
            Assert.IsNull(runtime.GetType("Fofuxo.GameplayAbilitySystem.AbilityHitInfo"));
            Assert.IsNull(runtime.GetType("Fofuxo.GameplayAbilitySystem.AbilityImpact"));
        }

        [Test]
        public void TheHistoryShowsTheHitFromBothEnds()
        {
            bool previous = AbilityDiagnostics.Enabled;
            AbilityDiagnostics.Enabled = true;
            try
            {
                GrantReaction();
                Hit(NewDamage(10f));

                Assert.IsTrue(Contains(attackerSystem, AbilityEventKind.EffectDelivered));
                Assert.IsTrue(Contains(targetSystem, AbilityEventKind.EffectApplied));
                Assert.IsTrue(Contains(targetSystem, AbilityEventKind.GameplayEvent));
                Assert.IsTrue(Contains(targetSystem, AbilityEventKind.AbilityStarted));
            }
            finally
            {
                AbilityDiagnostics.Enabled = previous;
            }
        }

        // ------------------------------------------------------------ helpers

        private static bool Contains(AbilitySystem system, AbilityEventKind kind)
        {
            if (!system.HasHistory)
            {
                return false;
            }

            for (int i = 0; i < system.History.Count; i++)
            {
                if (system.History[i].Kind == kind)
                {
                    return true;
                }
            }

            return false;
        }

        private static float HealthOf(GameObject actor)
        {
            return actor.GetComponent<AttributeSet>().GetCurrent(Health);
        }

        /// <summary>One swing from the attacker: a fresh activation, so dedup never eats a hit.</summary>
        private int Hit(DamageEffectDefinition effect)
        {
            TimelineAbilityDefinition swing = NewSwing();
            AbilityInstance instance = new(swing, AbilityContext.FromTarget(attacker, target));
            Physics.SyncTransforms();
            return effect.ApplyFrom(new GameplayEffectContext(attackerSystem, instance, 0));
        }

        private DamageEffectDefinition NewDamage(
            float amount,
            float horizontal = 0f,
            float duration = 0f,
            bool parryable = true)
        {
            DamageEffectDefinition effect = Own(ScriptableObject.CreateInstance<DamageEffectDefinition>());
            effect.name = "GE_Damage_Test";
            effect.SetTargetingForTests(GameplayEffectTargeting.FromShape(
                HitShape.Sphere(Vector3.zero, 4f).WithOrigin(HitShapeOrigin.AbilityAimPoint),
                8,
                false));
            effect.ConfigureDamageForTests(
                amount, Health, horizontal: horizontal, duration: duration, parryable: parryable);
            effect.SetKnockbackDirectionForTests(KnockbackDirection.AwayFromOwner);
            return effect;
        }

        private HitReactionAbilityDefinition GrantReaction(bool restartWhenActive = false)
        {
            HitReactionAbilityDefinition reaction =
                NewReaction("test.reaction.hit", restartWhenActive);
            Grant(reaction);
            return reaction;
        }

        private HitReactionAbilityDefinition NewReaction(string id, bool restartWhenActive = false)
        {
            HitReactionAbilityDefinition reaction =
                Own(ScriptableObject.CreateInstance<HitReactionAbilityDefinition>());
            reaction.SetAbilityIdForTests(id);
            SetField(reaction, "requiresTarget", false);
            SetField(reaction, "grantedTags", new[] { Stunned });
            SetField(reaction, "blockedTags", new[]
            {
                CommonGameplayTags.Dead, CommonGameplayTags.Invulnerable,
            });
            reaction.SetExclusionGroupForTests(default, AbilityGroupExclusionPolicy.CancelAnyActive);
            reaction.SetActivationTriggersForTests(new AbilityActivationTrigger(
                CommonGameplayTags.HitReactionEvent,
                true,
                restartWhenActive: restartWhenActive));
            AbilityStep step = new();
            step.ConfigureForTests(1, 2, 30, 60f);
            reaction.SetStepsForTests(step);
            return reaction;
        }

        private TimelineAbilityDefinition NewSwing()
        {
            TimelineAbilityDefinition swing = Own(ScriptableObject.CreateInstance<TimelineAbilityDefinition>());
            swing.SetAbilityIdForTests("test.swing");
            SetField(swing, "requiresTarget", false);
            AbilityStep step = new();
            step.ConfigureForTests(1, 2, 60, 60f);
            swing.SetStepsForTests(step);
            return swing;
        }

        private void Grant(params AbilityDefinition[] abilities)
        {
            AbilityLoadout loadout = Own(ScriptableObject.CreateInstance<AbilityLoadout>());
            SetField(loadout, "abilities", abilities);
            SetField(targetSystem, "loadout", loadout);
        }

        private T Own<T>(T item) where T : Object
        {
            owned.Add(item);
            return item;
        }

        private static void SetField<TValue>(object obj, string fieldName, TValue value)
        {
            for (System.Type type = obj.GetType(); type != null; type = type.BaseType)
            {
                System.Reflection.FieldInfo field = type.GetField(
                    fieldName,
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    field.SetValue(obj, value);
                    return;
                }
            }

            Assert.Fail($"No field named {fieldName}.");
        }

        private sealed class RecordingMotor : IAbilityMotor
        {
            public Vector3 TotalDelta { get; private set; }

            public Vector3 Position => TotalDelta;

            public Vector3 Move(Vector3 delta, AbilityMovementCollision collision)
            {
                TotalDelta += delta;
                return delta;
            }
        }
    }
}
