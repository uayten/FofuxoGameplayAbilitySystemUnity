using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    /// <summary>
    /// Covers the shared target acquisition: one physics query per trigger
    /// frame, a deterministic order over its result, and the filters every
    /// consumer runs on top of it.
    /// </summary>
    public sealed class AbilityTargetDataTests
    {
        /// <summary>
        /// Makes a collider an actor: something with attributes above it. The
        /// filter skips scenery by default, and whether an actor can take a hit
        /// is each effect's question, not the acquisition's.
        /// </summary>
        private sealed class RecordingReceiver : AttributeSet
        {
        }

        /// <summary>
        /// Reports the acquisition it was handed, so a test can tell one shared
        /// result from two independent ones.
        /// </summary>
        private sealed class ProbeEffect : GameplayEffectDefinition
        {
            public readonly List<GameObject> Actors = new();

            /// <summary>
            /// A property rather than a field: Unity cannot serialize the
            /// acquisition, and a public field would only earn a warning.
            /// </summary>
            public AbilityTargetData Received { get; private set; }

            protected override void OnTargetsAcquired(
                AbilityTargetData targets, in GameplayEffectContext context)
            {
                Received = targets;
                Actors.Clear();
                for (int i = 0; i < targets.Count; i++)
                {
                    Actors.Add(targets[i].Actor);
                }
            }
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

        /// <summary>
        /// The regression this milestone exists for. Two effects on one trigger
        /// frame used to run one physics query each, so the fourth hit of a
        /// combo could damage one enemy and push another. The world moves
        /// between the two applications here: the second effect still sees what
        /// the first one saw, because there was only ever one query.
        /// </summary>
        [Test]
        public void SameShapeOnOneFrame_RunsOneQueryBothEffectsRead()
        {
            GameObject owner = NewObject("QueryOwner");
            RecordingReceiver near = NewTarget("Near", new Vector3(0f, 0f, 1f));
            RecordingReceiver far = NewTarget("Far", new Vector3(0f, 0f, 2f));
            HitShape shape = HitShape.Sphere(new Vector3(0f, 0f, 1.5f), 3f);

            ProbeEffect first = NewProbe(shape);
            ProbeEffect second = NewProbe(shape);
            AbilityInstance instance = NewInstance(owner);
            AbilitySystem system = owner.AddComponent<AbilitySystem>();
            Physics.SyncTransforms();

            first.ApplyFrom(new GameplayEffectContext(system, instance, 0));
            far.transform.position = new Vector3(0f, 0f, 60f);
            Physics.SyncTransforms();
            second.ApplyFrom(new GameplayEffectContext(system, instance, 1));

            Assert.AreSame(first.Received, second.Received);
            CollectionAssert.AreEqual(first.Actors, second.Actors);
            Assert.Contains(near.gameObject, second.Actors);
            Assert.Contains(far.gameObject, second.Actors, "The query was run twice.");
        }

        /// <summary>
        /// Sharing is per shape, not per frame: an effect with its own volume
        /// still gets its own query, because the author asked for two volumes.
        /// </summary>
        [Test]
        public void DifferentShapeOnOneFrame_RunsItsOwnQuery()
        {
            GameObject owner = NewObject("QueryOwner");
            NewTarget("Near", new Vector3(0f, 0f, 1f));
            NewTarget("Far", new Vector3(0f, 0f, 4f));

            ProbeEffect wide = NewProbe(HitShape.Sphere(Vector3.zero, 6f));
            ProbeEffect narrow = NewProbe(HitShape.Sphere(Vector3.zero, 2f));
            AbilityInstance instance = NewInstance(owner);
            AbilitySystem system = owner.AddComponent<AbilitySystem>();
            Physics.SyncTransforms();

            wide.ApplyFrom(new GameplayEffectContext(system, instance, 0));
            narrow.ApplyFrom(new GameplayEffectContext(system, instance, 1));

            Assert.AreEqual(2, wide.Actors.Count);
            Assert.AreEqual(1, narrow.Actors.Count);
        }

        /// <summary>
        /// Physics returns overlaps in an unspecified order. Two candidates at
        /// the same distance and angle have to come out the same way every time,
        /// or two effects reading one result still pick different targets.
        /// </summary>
        [Test]
        public void EqualDistanceCandidates_KeepAStableOrder()
        {
            GameObject owner = NewObject("SortOwner");
            RecordingReceiver left = NewTarget("Left", new Vector3(-1f, 0f, 0f));
            RecordingReceiver right = NewTarget("Right", new Vector3(1f, 0f, 0f));
            Physics.SyncTransforms();

            AbilityTargetQuery query = new(HitShape.Sphere(Vector3.zero, 4f), default);
            AbilityTargetData first = new();
            AbilityTargetData second = new();
            query.Resolve(owner, owner.transform.position, Vector3.forward, first);
            query.Resolve(owner, owner.transform.position, Vector3.forward, second);

            Assert.AreEqual(2, first.Count);
            Assert.AreEqual(first[0].Actor, second[0].Actor);
            Assert.AreEqual(first[1].Actor, second[1].Actor);
            Assert.AreEqual(first[0].Distance, first[1].Distance, 1e-4f);

            GameObject expectedFirst =
                left.gameObject.GetEntityId() < right.gameObject.GetEntityId()
                    ? left.gameObject
                    : right.gameObject;
            Assert.AreEqual(expectedFirst, first[0].Actor);
        }

        [Test]
        public void NearestCandidateRanksFirst()
        {
            GameObject owner = NewObject("SortOwner");
            NewTarget("Far", new Vector3(0f, 0f, 5f));
            RecordingReceiver near = NewTarget("Near", new Vector3(0f, 0f, 2f));
            Physics.SyncTransforms();

            AbilityTargetData data = new();
            new AbilityTargetQuery(HitShape.Sphere(Vector3.zero, 8f), default)
                .Resolve(owner, owner.transform.position, Vector3.forward, data);

            Assert.AreEqual(near.gameObject, data.PrimaryActor);
        }

        [Test]
        public void OwnerIsExcludedUnlessTheFilterAsksForIt()
        {
            GameObject owner = NewObject("FilterOwner");
            owner.AddComponent<BoxCollider>();
            owner.AddComponent<RecordingReceiver>();
            NewTarget("Enemy", new Vector3(0f, 0f, 2f));
            Physics.SyncTransforms();

            AbilityTargetData excluded = new();
            new AbilityTargetQuery(HitShape.Sphere(Vector3.zero, 6f), default)
                .Resolve(owner, owner.transform.position, Vector3.forward, excluded);
            Assert.AreEqual(1, excluded.Count);

            AbilityTargetData included = new();
            new AbilityTargetQuery(
                    HitShape.Sphere(Vector3.zero, 6f),
                    NewFilter(includeOwner: true))
                .Resolve(owner, owner.transform.position, Vector3.forward, included);
            Assert.AreEqual(2, included.Count);
        }

        [Test]
        public void BlockedTagOnTheTargetRejectsIt()
        {
            GameObject owner = NewObject("FilterOwner");
            RecordingReceiver blocked = NewTarget("Blocked", new Vector3(0f, 0f, 2f));
            AbilitySystem blockedSystem = blocked.gameObject.AddComponent<AbilitySystem>();
            blockedSystem.SetLooseTag(CommonGameplayTags.Invulnerable, true);
            NewTarget("Open", new Vector3(0f, 0f, 3f));
            Physics.SyncTransforms();

            AbilityTargetData data = new();
            new AbilityTargetQuery(
                    HitShape.Sphere(Vector3.zero, 8f),
                    default(AbilityTargetFilter).WithBlockedTags(
                        CommonGameplayTags.Invulnerable))
                .Resolve(owner, owner.transform.position, Vector3.forward, data);

            Assert.AreEqual(1, data.Count);
            Assert.AreNotEqual(blocked.gameObject, data.PrimaryActor);
        }

        [Test]
        public void MaximumCountTrimsToTheNearest()
        {
            GameObject owner = NewObject("FilterOwner");
            RecordingReceiver near = NewTarget("Near", new Vector3(0f, 0f, 1f));
            NewTarget("Mid", new Vector3(0f, 0f, 3f));
            NewTarget("Far", new Vector3(0f, 0f, 5f));
            Physics.SyncTransforms();

            AbilityTargetData data = new();
            new AbilityTargetQuery(
                    HitShape.Sphere(Vector3.zero, 9f),
                    default(AbilityTargetFilter).WithMaximumCount(2))
                .Resolve(owner, owner.transform.position, Vector3.forward, data);

            Assert.AreEqual(2, data.Count);
            Assert.AreEqual(near.gameObject, data.PrimaryActor);
        }

        [Test]
        public void LineOfSightDropsTargetsBehindGeometry()
        {
            GameObject owner = NewObject("FilterOwner");
            RecordingReceiver target = NewTarget("Hidden", new Vector3(0f, 0f, 5f));
            AbilityTargetQuery query = new(
                HitShape.Sphere(Vector3.zero, 9f),
                NewFilter(requireLineOfSight: true));

            Physics.SyncTransforms();
            AbilityTargetData clear = new();
            query.Resolve(owner, owner.transform.position, Vector3.forward, clear);
            Assert.AreEqual(target.gameObject, clear.PrimaryActor);

            GameObject wall = NewObject("Wall");
            wall.transform.position = new Vector3(0f, 0f, 2.5f);
            wall.transform.localScale = new Vector3(6f, 6f, 0.2f);
            wall.AddComponent<BoxCollider>();
            Physics.SyncTransforms();

            AbilityTargetData blocked = new();
            query.Resolve(owner, owner.transform.position, Vector3.forward, blocked);
            Assert.AreEqual(0, blocked.Count);
        }

        /// <summary>
        /// The cone form target assist runs: inside the inner radius the angle
        /// stops mattering, which is how an enemy beside the owner stays a
        /// melee candidate.
        /// </summary>
        [Test]
        public void ConeAcceptsInsideTheInnerRadiusAndRejectsOutsideTheAngle()
        {
            GameObject owner = NewObject("ConeOwner");
            RecordingReceiver ahead = NewTarget("Ahead", new Vector3(0f, 0f, 4f));
            RecordingReceiver beside = NewTarget("Beside", new Vector3(1.2f, 0f, 0f));
            RecordingReceiver away = NewTarget("Away", new Vector3(5f, 0f, 0f));
            Physics.SyncTransforms();

            AbilityTargetData data = new();
            new AbilityTargetQuery(HitShape.Cone(0, 8f, 30f, 2f), default)
                .Resolve(owner, owner.transform.position, Vector3.forward, data);

            Assert.IsTrue(data.Contains(ahead.gameObject));
            Assert.IsTrue(data.Contains(beside.gameObject));
            Assert.IsFalse(data.Contains(away.gameObject));
        }

        [Test]
        public void SnapshotSurvivesTheNextAcquisition()
        {
            GameObject owner = NewObject("SnapshotOwner");
            NewTarget("Enemy", new Vector3(0f, 0f, 2f));
            Physics.SyncTransforms();

            AbilityTargetData live = new();
            AbilityTargetQuery query = new(HitShape.Sphere(Vector3.zero, 6f), default);
            query.Resolve(owner, owner.transform.position, Vector3.forward, live);
            AbilityTargetData snapshot = live.Snapshot();

            owner.transform.position = new Vector3(0f, 0f, 100f);
            Physics.SyncTransforms();
            query.Resolve(owner, owner.transform.position, Vector3.forward, live);

            Assert.AreEqual(0, live.Count);
            Assert.AreEqual(1, snapshot.Count);
        }

        [Test]
        public void PruneDestroyedRemovesLostActors()
        {
            GameObject owner = NewObject("PruneOwner");
            RecordingReceiver target = NewTarget("Enemy", new Vector3(0f, 0f, 2f));
            Physics.SyncTransforms();

            AbilityTargetData data = new();
            new AbilityTargetQuery(HitShape.Sphere(Vector3.zero, 6f), default)
                .Resolve(owner, owner.transform.position, Vector3.forward, data);
            Assert.AreEqual(1, data.Count);

            Object.DestroyImmediate(target.gameObject);

            Assert.AreEqual(1, data.PruneDestroyed());
            Assert.AreEqual(0, data.Count);
        }

        /// <summary>
        /// External acquisition: the caller already knows who to hit, so the
        /// ability keeps the timeline and skips the search entirely.
        /// </summary>
        [Test]
        public void SuppliedTargetsReplaceLocalAcquisition()
        {
            GameObject owner = NewObject("SupplyOwner");
            GameObject chosen = NewObject("Chosen");
            chosen.transform.position = new Vector3(0f, 0f, 12f);
            AbilitySystem system = owner.AddComponent<AbilitySystem>();
            TimelineAbilityDefinition ability = NewAbility("test.supply");
            Grant(system, ability);

            AbilityTargetData supplied = AbilityTargetData.FromActors(
                owner.transform.position, Vector3.forward, chosen);

            Assert.IsTrue(system.TryActivateWithTargets(
                ability,
                AbilityContext.FromDirection(owner, null, Vector3.forward),
                supplied,
                out _));

            Assert.AreEqual(chosen, system.ActiveTargetData?.PrimaryActor);
            Assert.AreEqual(chosen, system.ActiveContext?.Target);
        }

        /// <summary>
        /// Target assist stops writing loose fields into the context: it
        /// produces target data, and the context is read back off it.
        /// </summary>
        [Test]
        public void TargetAssistPublishesItsAcquisitionAsTargetData()
        {
            GameObject owner = NewObject("AssistOwner");
            RecordingReceiver target = NewTarget("Enemy", new Vector3(0f, 0f, 3f));
            AbilitySystem system = owner.AddComponent<AbilitySystem>();
            TimelineAbilityDefinition ability = NewAbility("test.assist.data");
            TargetAssistDefinition assist = NewAssist(6f, 60f, 0f);
            ability.SetTargetAssistForTests(assist);
            Grant(system, ability);
            Physics.SyncTransforms();

            Assert.IsTrue(system.TryActivate(
                ability, AbilityContext.FromDirection(owner, null, Vector3.forward)));

            Assert.AreEqual(target.gameObject, system.ActiveTargetData?.PrimaryActor);
            Assert.AreEqual(target.gameObject, system.ActiveContext?.Target);
        }

        /// <summary>
        /// A locked assist keeps the enemy the activation committed to even
        /// when a closer one walks in for the next step.
        /// </summary>
        [Test]
        public void LockedAssistKeepsTheFirstTargetAcrossSteps()
        {
            GameObject owner = NewObject("LockOwner");
            RecordingReceiver first = NewTarget("First", new Vector3(0f, 0f, 3f));
            AbilitySystem system = owner.AddComponent<AbilitySystem>();
            TargetAssistDefinition assist = NewAssist(8f, 80f, 0f);
            SetField(assist, "lockPolicy", AbilityTargetLockPolicy.Lock);
            SetField(assist, "approachTarget", false);

            TimelineAbilityDefinition ability = NewAbility("test.assist.lock", stepCount: 2);
            foreach (AbilityStep step in ability.Steps)
            {
                step.SetTargetAssistForTests(assist);
            }

            Grant(system, ability);
            Physics.SyncTransforms();
            Assert.IsTrue(system.TryActivate(
                ability, AbilityContext.FromDirection(owner, null, Vector3.forward)));
            Assert.AreEqual(first.gameObject, system.ActiveContext?.Target);

            NewTarget("Closer", new Vector3(0f, 0f, 1f));
            Physics.SyncTransforms();
            system.Tick(1f);

            Assert.AreEqual(1, system.ActiveStepIndex);
            Assert.AreEqual(first.gameObject, system.ActiveContext?.Target);
        }

        /// <summary>
        /// The filter's booleans have no fluent setter, so they are written on a
        /// boxed copy and unboxed back out — reflection on a struct otherwise
        /// writes to a copy that is thrown away.
        /// </summary>
        private static AbilityTargetFilter NewFilter(
            bool includeOwner = false,
            bool requireLineOfSight = false)
        {
            object boxed = default(AbilityTargetFilter);
            SetField(boxed, "includeOwner", includeOwner);
            SetField(boxed, "requireLineOfSight", requireLineOfSight);
            return (AbilityTargetFilter)boxed;
        }

        private ProbeEffect NewProbe(HitShape shape)
        {
            ProbeEffect effect = ScriptableObject.CreateInstance<ProbeEffect>();
            effect.SetTargetingForTests(
                GameplayEffectTargeting.FromShape(shape, AbilityTargetQuery.Capacity, false));
            owned.Add(effect);
            return effect;
        }

        private AbilityInstance NewInstance(GameObject owner)
        {
            TimelineAbilityDefinition definition = NewAbility("test.targeting.probe");
            return new AbilityInstance(
                definition,
                AbilityContext.FromDirection(owner, null, Vector3.forward));
        }

        private RecordingReceiver NewTarget(string name, Vector3 position)
        {
            GameObject target = NewObject(name);
            target.transform.position = position;
            BoxCollider collider = target.AddComponent<BoxCollider>();
            collider.isTrigger = false;
            return target.AddComponent<RecordingReceiver>();
        }

        private TargetAssistDefinition NewAssist(
            float searchDistance,
            float coneHalfAngle,
            float proximityRadius)
        {
            TargetAssistDefinition assist =
                ScriptableObject.CreateInstance<TargetAssistDefinition>();
            owned.Add(assist);
            LayerMask mask = default;
            mask.value = ~0;
            SetField(assist, "targetLayers", mask);
            SetField(assist, "searchDistance", searchDistance);
            SetField(assist, "coneHalfAngle", coneHalfAngle);
            SetField(assist, "proximityRadius", proximityRadius);
            SetField(assist, "approachTarget", false);
            return assist;
        }

        private TimelineAbilityDefinition NewAbility(string id, int stepCount = 1)
        {
            TimelineAbilityDefinition ability = ScriptableObject.CreateInstance<TimelineAbilityDefinition>();
            owned.Add(ability);
            ability.SetAbilityIdForTests(id);
            SetField(ability, "requiresTarget", false);
            AbilityStep[] steps = new AbilityStep[stepCount];
            for (int i = 0; i < stepCount; i++)
            {
                steps[i] = new AbilityStep();
                steps[i].ConfigureForTests(1, 2, 30, 60f);
            }

            ability.SetStepsForTests(steps);
            return ability;
        }

        private GameObject NewObject(string name)
        {
            GameObject created = new(name);
            owned.Add(created);
            return created;
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
    }
}
