using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    /// <summary>
    /// The allocation and timing counters: they count what they are wrapped
    /// around, they say nothing while diagnostics are off, and the three
    /// instrumented sites — target queries, effect application, tasks — really
    /// do report to them.
    /// </summary>
    public sealed class AbilityDiagnosticCounterTests
    {
        private static readonly GameplayAttribute Health = new("Test.Health");

        private readonly List<Object> owned = new();
        private bool previousEnabled;

        [SetUp]
        public void SetUp()
        {
            previousEnabled = AbilityDiagnostics.Enabled;
            AbilityDiagnostics.Enabled = true;
            AbilityDiagnostics.ResetCounters();
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
            AbilityDiagnostics.ResetCounters();
            AbilityDiagnostics.Enabled = previousEnabled;
        }

        [Test]
        public void Sample_CountsOnce_AndTimesAreNeverNegative()
        {
            AbilityDiagnosticCounter counter = new("test");

            using (counter.Begin())
            {
            }

            Assert.AreEqual(1, counter.Count);
            Assert.GreaterOrEqual(counter.TotalTicks, 0);
            Assert.GreaterOrEqual(counter.MaxTicks, 0);
            Assert.GreaterOrEqual(counter.TotalMilliseconds, 0d);
            Assert.AreEqual(counter.TotalMilliseconds, counter.AverageMilliseconds, 1e-9);
        }

        [Test]
        public void Sample_WhileDisabled_IsInert()
        {
            AbilityDiagnosticCounter counter = new("test");
            AbilityDiagnostics.Enabled = false;

            using (counter.Begin())
            {
            }

            Assert.AreEqual(0, counter.Count);
            Assert.AreEqual(0, counter.TotalTicks);
        }

        [Test]
        public void Reset_ClearsEverything()
        {
            AbilityDiagnosticCounter counter = new("test");
            using (counter.Begin())
            {
            }

            counter.Reset();

            Assert.AreEqual(0, counter.Count);
            Assert.AreEqual(0, counter.TotalTicks);
            Assert.AreEqual(0, counter.MaxTicks);
            Assert.AreEqual(0, counter.AllocatedBytes);
            Assert.AreEqual(0d, counter.AverageMilliseconds);
        }

        /// <summary>
        /// The allocation figure is the Profiler's own counter, which exists in
        /// the Editor. A sample that allocates must say so; the exact number is
        /// the runtime's, so only the direction is pinned.
        /// </summary>
        [Test]
        public void Sample_ReportsManagedAllocation()
        {
            AbilityDiagnosticCounter counter = new("test");
            byte[] keep;

            using (counter.Begin())
            {
                keep = new byte[8192];
            }

            System.GC.KeepAlive(keep);
            Assert.Greater(counter.AllocatedBytes, 0);
        }

        [Test]
        public void TargetQuery_IsCounted()
        {
            GameObject owner = Own(new GameObject("QueryOwner"));
            AbilityTargetQuery query = new(HitShape.Sphere(Vector3.zero, 1f), default);
            AbilityTargetData data = new();
            long before = AbilityDiagnostics.TargetQueries.Count;

            query.Resolve(owner, owner.transform.position, Vector3.forward, data);

            Assert.AreEqual(before + 1, AbilityDiagnostics.TargetQueries.Count);
        }

        [Test]
        public void EffectApplication_IsCounted()
        {
            GameObject actor = Own(new GameObject("EffectActor"));
            actor.AddComponent<AttributeSet>().SetInitialValues(new[]
            {
                new AttributeSet.InitialValue(Health, 100f, 0f, 100f),
            });
            long before = AbilityDiagnostics.EffectApplications.Count;

            GameplayEffectContainer.For(actor).ApplyDynamic(
                GameplayEffectDurationPolicy.Instant, Health, AttributeOperation.Add, -1f);

            Assert.AreEqual(before + 1, AbilityDiagnostics.EffectApplications.Count);
            Assert.AreEqual(99f, actor.GetComponent<AttributeSet>().GetCurrent(Health), 1e-4f);
        }

        [Test]
        public void TaskStartAndTick_AreCounted()
        {
            GameObject owner = Own(new GameObject("TaskOwner"));
            AbilitySystem system = owner.AddComponent<AbilitySystem>();
            long before = AbilityDiagnostics.Tasks.Count;

            WaitDelayTask task = system.RunActorTask(new WaitDelayTask(5f));
            Assert.IsTrue(task.IsRunning);
            Assert.AreEqual(before + 1, AbilityDiagnostics.Tasks.Count, "the start is one sample");

            system.Tick(0.1f);

            Assert.AreEqual(before + 2, AbilityDiagnostics.Tasks.Count, "the tick is another");
        }

        [Test]
        public void DisabledDiagnostics_LeaveTheSiteCountersAlone()
        {
            AbilityDiagnostics.Enabled = false;
            GameObject owner = Own(new GameObject("QuietOwner"));
            AbilityTargetQuery query = new(HitShape.Sphere(Vector3.zero, 1f), default);

            query.Resolve(owner, owner.transform.position, Vector3.forward, new AbilityTargetData());

            Assert.AreEqual(0, AbilityDiagnostics.TargetQueries.Count);
        }

        [Test]
        public void ResetCounters_ResetsAllThree()
        {
            using (AbilityDiagnostics.TargetQueries.Begin())
            {
            }

            using (AbilityDiagnostics.EffectApplications.Begin())
            {
            }

            using (AbilityDiagnostics.Tasks.Begin())
            {
            }

            AbilityDiagnostics.ResetCounters();

            Assert.AreEqual(0, AbilityDiagnostics.TargetQueries.Count);
            Assert.AreEqual(0, AbilityDiagnostics.EffectApplications.Count);
            Assert.AreEqual(0, AbilityDiagnostics.Tasks.Count);
        }

        [Test]
        public void ToString_NamesTheCounter()
        {
            AbilityDiagnosticCounter counter = new("Target queries");

            StringAssert.StartsWith("Target queries", counter.ToString());
        }

        private T Own<T>(T item) where T : Object
        {
            owned.Add(item);
            return item;
        }
    }
}
