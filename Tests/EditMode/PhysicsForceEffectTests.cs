using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    public sealed class PhysicsForceEffectTests
    {
        private static readonly GameplayAttribute Health = new("Test.Health");

        /// <summary>Names a target; the force resolves its own physics body from it.</summary>
        private sealed class RecordingReceiver : MonoBehaviour
        {
        }

        private readonly List<Object> owned = new();

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

        [Test]
        public void LegacyTriggerLevel_ResolvesToOne()
        {
            Assert.AreEqual(1, default(AbilityEffectTrigger).Level);
        }

        [Test]
        public void EffectContext_ReceivesTheTriggerLevel()
        {
            GameObject owner = NewObject("LevelOwner");
            AbilitySystem system = owner.AddComponent<AbilitySystem>();
            CapturingEffect effect = ScriptableObject.CreateInstance<CapturingEffect>();
            owned.Add(effect);
            TimelineAbilityDefinition ability = NewAbility("test.force.level");
            ability.FirstStepForTests.SetEffectTriggersForTests(new[]
            {
                new AbilityEffectTrigger(1, effect, 73),
            });
            Grant(system, ability);

            system.TryActivate(ability, AbilityContext.FromTarget(owner, null));
            system.Tick(1f / 120f);

            Assert.AreEqual(73, effect.ReceivedLevel);
        }

        [Test]
        public void LevelHundred_ResolvesMoreVelocityThanLevelOne()
        {
            PhysicsForceEffectDefinition effect = NewForceEffect(
                PhysicsForceDirection.OwnerForward);

            Vector3 levelOne = effect.ResolveVelocityChange(1, Vector3.forward);
            Vector3 levelHundred = effect.ResolveVelocityChange(100, Vector3.forward);

            Assert.Greater(levelHundred.magnitude, levelOne.magnitude);
            Assert.Greater(levelHundred.y, levelOne.y);
        }

        [Test]
        public void PushAndPull_ResolveOppositeDirections()
        {
            PhysicsForceEffectDefinition push = NewForceEffect(
                PhysicsForceDirection.AwayFromOwner);
            PhysicsForceEffectDefinition pull = NewForceEffect(
                PhysicsForceDirection.TowardOwner);
            GameObject owner = NewObject("DirectionOwner");
            Vector3 targetPosition = Vector3.right * 3f;

            Vector3 pushVelocity = push.ResolveVelocityChange(
                50,
                push.ResolveDirectionForTests(
                    owner.transform,
                    owner.transform.position,
                    targetPosition));
            Vector3 pullVelocity = pull.ResolveVelocityChange(
                50,
                pull.ResolveDirectionForTests(
                    owner.transform,
                    owner.transform.position,
                    targetPosition));

            Assert.Less(Vector3.Dot(pushVelocity.normalized, pullVelocity.normalized), -0.5f);
        }

        [Test]
        public void PhysicsBody_RestoresKinematicStateAndCompletesReactionWhenSettled()
        {
            GameObject target = NewObject("PhysicsTarget");
            Rigidbody rigidbody = target.AddComponent<Rigidbody>();
            rigidbody.isKinematic = true;
            rigidbody.useGravity = false;
            rigidbody.linearDamping = 0.25f;
            target.AddComponent<BoxCollider>();
            AbilitySystem system = target.AddComponent<AbilitySystem>();
            AbilityPhysicsBody physicsBody = target.AddComponent<AbilityPhysicsBody>();
            physicsBody.ConfigureForTests(0f, 5f, 1f, 1);
            TimelineAbilityDefinition reaction = NewReactionAbility();
            Grant(system, reaction);
            system.TryHandleGameplayEvent(
                CommonGameplayTags.PhysicsForceEvent,
                AbilityContext.FromDirection(target, null, Vector3.forward),
                out AbilityDefinition triggered);

            Assert.IsTrue(physicsBody.ApplyVelocityChange(
                new Vector3(2f, 3f, 0f),
                triggered,
                PhysicsForceApplication.ReplaceVelocity));
            Assert.IsTrue(physicsBody.IsActive);
            Assert.IsFalse(rigidbody.isKinematic);
            Assert.IsTrue(rigidbody.useGravity);
            Assert.AreEqual(2f, rigidbody.linearDamping);
            Assert.AreEqual(
                CollisionDetectionMode.ContinuousDynamic,
                rigidbody.collisionDetectionMode);
            Assert.IsTrue(system.HasTag(CommonGameplayTags.PhysicsControlled));

            rigidbody.linearVelocity = Vector3.zero;
            physicsBody.TickForTests(0.02f, isGrounded: true);

            Assert.IsFalse(physicsBody.IsActive);
            Assert.IsTrue(rigidbody.isKinematic);
            Assert.IsFalse(rigidbody.useGravity);
            Assert.AreEqual(0.25f, rigidbody.linearDamping);
            Assert.IsFalse(system.IsActive);
            Assert.IsFalse(system.HasTag(CommonGameplayTags.PhysicsControlled));
        }

        [Test]
        public void PhysicsBody_EmitsOneControlPairAcrossRepeatedForces()
        {
            GameObject target = NewObject("RepeatedForceTarget");
            Rigidbody rigidbody = target.AddComponent<Rigidbody>();
            target.AddComponent<BoxCollider>();
            AbilitySystem system = target.AddComponent<AbilitySystem>();
            AbilityPhysicsBody physicsBody = target.AddComponent<AbilityPhysicsBody>();
            physicsBody.ConfigureForTests(0f, 5f, 1f, 1);
            TimelineAbilityDefinition reaction = NewReactionAbility();
            Grant(system, reaction);
            system.TryHandleGameplayEvent(
                CommonGameplayTags.PhysicsForceEvent,
                AbilityContext.FromTarget(target, null),
                out AbilityDefinition triggered);

            int started = 0;
            int ended = 0;
            physicsBody.PhysicsControlStarted += () => started++;
            physicsBody.PhysicsControlEnded += () => ended++;

            physicsBody.ApplyVelocityChange(Vector3.right, triggered, PhysicsForceApplication.Accumulate);
            physicsBody.ApplyVelocityChange(Vector3.forward, triggered, PhysicsForceApplication.Accumulate);
            rigidbody.linearVelocity = Vector3.zero;
            physicsBody.TickForTests(0.02f, isGrounded: true);

            Assert.AreEqual(1, started);
            Assert.AreEqual(1, ended);
        }

        [Test]
        public void ForceEffect_ActivatesReactionOnceForMultipleTargetColliders()
        {
            GameObject source = NewObject("ForceSource");
            AbilitySystem sourceSystem = source.AddComponent<AbilitySystem>();
            TimelineAbilityDefinition sourceAbility = NewAbility("test.force.source");
            var sourceInstance = new AbilityInstance(
                sourceAbility,
                AbilityContext.FromDirection(source, null, Vector3.forward));

            GameObject target = NewObject("ForceTarget");
            target.transform.position = Vector3.forward;
            target.AddComponent<Rigidbody>().isKinematic = true;
            target.AddComponent<BoxCollider>();
            target.AddComponent<RecordingReceiver>();
            GameObject child = NewObject("DamageHitbox", target.transform);
            child.AddComponent<BoxCollider>();
            AbilitySystem targetSystem = target.AddComponent<AbilitySystem>();
            AbilityPhysicsBody physicsBody = target.AddComponent<AbilityPhysicsBody>();
            TimelineAbilityDefinition reaction = NewReactionAbility();
            Grant(targetSystem, reaction);

            int started = 0;
            physicsBody.PhysicsControlStarted += () => started++;
            PhysicsForceEffectDefinition effect = NewForceEffect(
                PhysicsForceDirection.AwayFromOwner);
            Physics.SyncTransforms();

            effect.ApplyFrom(new GameplayEffectContext(sourceSystem, sourceInstance, 0, 10));

            Assert.AreEqual(1, started);
            Assert.AreSame(reaction, targetSystem.ActiveAbility);
            Assert.IsTrue(targetSystem.HasTag(CommonGameplayTags.PhysicsControlled));
        }

        [Test]
        public void ForceEffect_IgnoresTargetsWithBlockedTags()
        {
            GameObject source = NewObject("BlockedForceSource");
            AbilitySystem sourceSystem = source.AddComponent<AbilitySystem>();
            TimelineAbilityDefinition sourceAbility = NewAbility("test.force.blocked-source");
            var sourceInstance = new AbilityInstance(
                sourceAbility,
                AbilityContext.FromDirection(source, null, Vector3.forward));

            GameObject target = NewObject("BlockedForceTarget");
            target.transform.position = Vector3.forward;
            target.AddComponent<Rigidbody>().isKinematic = true;
            target.AddComponent<BoxCollider>();
            target.AddComponent<RecordingReceiver>();
            AbilitySystem targetSystem = target.AddComponent<AbilitySystem>();
            AbilityPhysicsBody physicsBody = target.AddComponent<AbilityPhysicsBody>();
            TimelineAbilityDefinition reaction = NewReactionAbility();
            Grant(targetSystem, reaction);
            targetSystem.SetLooseTag(CommonGameplayTags.Blocking, true);

            PhysicsForceEffectDefinition effect = NewForceEffect(
                PhysicsForceDirection.AwayFromOwner);
            effect.SetBlockedTargetTagsForTests(CommonGameplayTags.Blocking);
            Physics.SyncTransforms();

            effect.ApplyFrom(new GameplayEffectContext(sourceSystem, sourceInstance, 0, 10));

            Assert.IsFalse(physicsBody.IsActive);
            Assert.IsFalse(targetSystem.IsActive);
        }

        [Test]
        public void VerticalLaunch_IsIndependentFromHorizontalDirection()
        {
            PhysicsForceEffectDefinition effect = NewForceEffect(
                PhysicsForceDirection.AwayFromOwner);

            Vector3 forward = effect.ResolveVelocityChange(60, Vector3.forward);
            Vector3 right = effect.ResolveVelocityChange(60, Vector3.right);

            Assert.AreEqual(forward.y, right.y, 1e-4f);
            Assert.Less(
                Vector3.Dot(
                    Vector3.ProjectOnPlane(forward, Vector3.up).normalized,
                    Vector3.ProjectOnPlane(right, Vector3.up).normalized),
                0.5f);
        }

        [Test]
        public void ForceApplication_DoesNotMutateTheEffectAsset()
        {
            GameObject source = NewObject("ImmutableForceSource");
            AbilitySystem sourceSystem = source.AddComponent<AbilitySystem>();
            TimelineAbilityDefinition sourceAbility = NewAbility("test.force.immutable");
            var sourceInstance = new AbilityInstance(
                sourceAbility,
                AbilityContext.FromDirection(source, null, Vector3.forward));
            NewForceTarget("ImmutableForceTarget");
            PhysicsForceEffectDefinition effect = NewForceEffect(
                PhysicsForceDirection.AwayFromOwner);
            string before = JsonUtility.ToJson(effect);
            Physics.SyncTransforms();

            effect.ApplyFrom(new GameplayEffectContext(sourceSystem, sourceInstance, 0, 40));

            Assert.AreEqual(before, JsonUtility.ToJson(effect));
        }

        [Test]
        public void CancellingTheSourceAbility_KeepsTheAppliedImpulse()
        {
            GameObject source = NewObject("CancelledForceSource");
            AbilitySystem sourceSystem = source.AddComponent<AbilitySystem>();
            PhysicsForceEffectDefinition effect = NewForceEffect(
                PhysicsForceDirection.AwayFromOwner);
            TimelineAbilityDefinition sourceAbility = NewAbility("test.force.cancelled");
            sourceAbility.FirstStepForTests.SetEffectTriggersForTests(new[]
            {
                new AbilityEffectTrigger(1, effect, 40),
            });
            Grant(sourceSystem, sourceAbility);

            AbilityPhysicsBody physicsBody = NewForceTarget("CancelledForceTarget");
            AbilitySystem targetSystem = physicsBody.AbilitySystem;
            Physics.SyncTransforms();

            sourceSystem.TryActivate(
                sourceAbility,
                AbilityContext.FromDirection(source, null, Vector3.forward));
            sourceSystem.Tick(1f / 120f);
            Assert.IsTrue(physicsBody.IsActive, "The force never reached the target.");

            sourceSystem.TryCancelActiveAbility(CommonGameplayTags.CancelManual);

            Assert.IsTrue(physicsBody.IsActive);
            Assert.IsTrue(targetSystem.HasTag(CommonGameplayTags.PhysicsControlled));
        }

        /// <summary>
        /// The fourth hit of a combo fires damage and a physics force on one
        /// frame. Each used to run its own overlap, so they could resolve
        /// different enemies out of what the author wrote as one volume: one
        /// took the damage, another one got pushed. Both now read the
        /// activation's shared acquisition.
        /// </summary>
        [Test]
        public void DamageAndForceOnOneFrame_ResolveTheSameTarget()
        {
            GameObject owner = NewObject("ComboOwner");
            AbilitySystem system = owner.AddComponent<AbilitySystem>();
            AbilityPhysicsBody near = NewForceTarget("Near", new Vector3(0f, 0f, 1f));
            AbilityPhysicsBody far = NewForceTarget("Far", new Vector3(0.4f, 0f, 1.6f));

            // Spelled out rather than left empty: an unset mask is an authoring
            // error now, and these targets are on whatever layer the test scene
            // gives them.
            HitShape shape = HitShape
                .Sphere(new Vector3(0f, 0f, 1.4f), 3f)
                .WithLayers(Physics.AllLayers);
            PhysicsForceEffectDefinition force = NewForceEffect(
                PhysicsForceDirection.AwayFromOwner);
            force.ConfigureForTests(
                shape,
                PhysicsForceDirection.AwayFromOwner,
                AnimationCurve.Linear(1f, 1f, 100f, 25f),
                AnimationCurve.Linear(1f, 0f, 100f, 12f));

            DamageEffectDefinition damage =
                ScriptableObject.CreateInstance<DamageEffectDefinition>();
            owned.Add(damage);
            damage.ConfigureDamageForTests(10f, Health);
            damage.SetTargetingForTests(
                GameplayEffectTargeting.FromShape(shape, 1, false));

            TimelineAbilityDefinition ability = NewAbility("test.combo.shared");
            ability.FirstStepForTests.SetEffectTriggersForTests(new[]
            {
                new AbilityEffectTrigger(1, damage, 1),
                new AbilityEffectTrigger(1, force, 20),
            });
            Grant(system, ability);
            Physics.SyncTransforms();

            system.TryActivate(ability, AbilityContext.FromTarget(owner, null));
            system.Tick(1f / 120f);

            AbilityPhysicsBody pushed = near.IsActive ? near : far.IsActive ? far : null;
            Assert.IsNotNull(pushed, "Neither target received the force.");
            Assert.AreNotEqual(near.IsActive, far.IsActive, "Both targets were pushed.");

            Assert.AreEqual(
                90f,
                pushed.GetComponent<AttributeSet>().GetCurrent(Health),
                1e-3f,
                "Damage and force landed on different targets.");
        }

        /// <summary>
        /// A target one unit ahead of the origin, complete enough for the force
        /// effect to accept it: receiver, collider, Rigidbody, ability system,
        /// physics body, and a granted reaction.
        /// </summary>
        private AbilityPhysicsBody NewForceTarget(string name)
        {
            return NewForceTarget(name, Vector3.forward);
        }

        private AbilityPhysicsBody NewForceTarget(string name, Vector3 position)
        {
            GameObject target = NewObject(name);
            target.transform.position = position;
            target.AddComponent<Rigidbody>().isKinematic = true;
            target.AddComponent<BoxCollider>();
            target.AddComponent<RecordingReceiver>();
            target.AddComponent<AttributeSet>().SetInitialValues(new[]
            {
                new AttributeSet.InitialValue(Health, 100f, 0f, 100f),
            });
            AbilitySystem targetSystem = target.AddComponent<AbilitySystem>();
            AbilityPhysicsBody physicsBody = target.AddComponent<AbilityPhysicsBody>();
            Grant(targetSystem, NewReactionAbility());
            return physicsBody;
        }

        private PhysicsForceEffectDefinition NewForceEffect(
            PhysicsForceDirection direction)
        {
            PhysicsForceEffectDefinition effect =
                ScriptableObject.CreateInstance<PhysicsForceEffectDefinition>();
            owned.Add(effect);
            effect.ConfigureForTests(
                HitShape.Sphere(Vector3.forward, 2f).WithLayers(Physics.AllLayers),
                direction,
                AnimationCurve.Linear(1f, 1f, 100f, 25f),
                AnimationCurve.Linear(1f, 0f, 100f, 12f));
            return effect;
        }

        private TimelineAbilityDefinition NewReactionAbility()
        {
            TimelineAbilityDefinition reaction = NewAbility("test.physics.reaction");
            reaction.SetActivationTriggersForTests(
                new AbilityActivationTrigger(CommonGameplayTags.PhysicsForceEvent, true));
            SetField(reaction, "grantedTags", new[]
            {
                CommonGameplayTags.PhysicsControlled,
                CommonGameplayTags.MovementLocked,
            });
            return reaction;
        }

        private TimelineAbilityDefinition NewAbility(string id)
        {
            TimelineAbilityDefinition ability = ScriptableObject.CreateInstance<TimelineAbilityDefinition>();
            owned.Add(ability);
            ability.SetAbilityIdForTests(id);
            SetField(ability, "requiresTarget", false);
            var step = new AbilityStep();
            step.ConfigureForTests(1, 2, 600, 60f);
            ability.SetStepsForTests(step);
            return ability;
        }

        private GameObject NewObject(string name, Transform parent = null)
        {
            var gameObject = new GameObject(name);
            if (parent != null)
            {
                gameObject.transform.SetParent(parent);
            }

            owned.Add(gameObject);
            return gameObject;
        }

        private void Grant(AbilitySystem system, params AbilityDefinition[] abilities)
        {
            AbilityLoadout loadout = ScriptableObject.CreateInstance<AbilityLoadout>();
            owned.Add(loadout);
            SetField(loadout, "abilities", abilities);
            SetField(system, "loadout", loadout);
        }

        private static void SetField<TValue>(object target, string name, TValue value)
        {
            // Walks up the hierarchy: a private field of a base class is
            // invisible to a single GetField call, and an ability's own fields
            // sit one level above the timeline type most fixtures use.
            for (System.Type type = target.GetType(); type != null; type = type.BaseType)
            {
                System.Reflection.FieldInfo field = type.GetField(
                    name,
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    field.SetValue(target, value);
                    return;
                }
            }

            Assert.Fail($"No field named {name}.");
        }

        private sealed class CapturingEffect : GameplayEffectDefinition
        {
            public int ReceivedLevel { get; private set; }

            protected override bool Execute(GameplayEffectSpec spec, bool periodic)
            {
                ReceivedLevel = spec.Level;
                return true;
            }
        }
    }
}
