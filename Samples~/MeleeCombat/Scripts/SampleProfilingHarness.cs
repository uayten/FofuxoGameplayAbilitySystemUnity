using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem.Samples
{
    /// <summary>
    /// The profiling load: a grid of fighters that pair off and fight, with the
    /// package's own counters on screen. The three axes the package cares about
    /// move with three fields - how many actors tick, how many effects are alive
    /// at once, and how often a target query runs - so one scene covers dense
    /// actors, simultaneous effects and target queries by being turned up.
    ///
    /// Read the numbers here, then open Window &gt; Fofuxo &gt; Ability Debugger
    /// for the same counters plus per-actor detail, and Unity's Profiler for the
    /// <c>GAS.TargetQuery</c>, <c>GAS.EffectApplication</c> and <c>GAS.Task</c>
    /// markers.
    /// </summary>
    public sealed class SampleProfilingHarness : MonoBehaviour
    {
        [Tooltip("The fighter spawned for both sides. The sample's enemy prefab already carries a brain.")]
        [SerializeField] private GameObject fighterPrefab;

        [Tooltip("Pairs of fighters. Every pair is two actors ticking, attacking and reacting.")]
        [SerializeField, Min(1)] private int pairCount = 16;

        [Tooltip("Metres between pairs.")]
        [SerializeField, Min(1f)] private float spacing = 5f;

        [Tooltip("Metres between the two fighters of a pair.")]
        [SerializeField, Min(1f)] private float pairDistance = 2.5f;

        [Tooltip("Reset the counters this many seconds after start, so the spawn itself is not measured.")]
        [SerializeField, Min(0f)] private float warmupSeconds = 2f;

        private bool warmedUp;
        private GUIStyle style;

        private void Start()
        {
            if (fighterPrefab == null)
            {
                Debug.LogWarning("No fighter prefab assigned; nothing to profile.", this);
                return;
            }

            AbilityDiagnostics.Enabled = true;
            int columns = Mathf.CeilToInt(Mathf.Sqrt(pairCount));
            for (int i = 0; i < pairCount; i++)
            {
                Vector3 origin = new(
                    (i % columns) * spacing,
                    1f,
                    (i / columns) * spacing);
                SpawnPair(origin);
            }
        }

        private void SpawnPair(Vector3 origin)
        {
            SampleFighter left = Spawn(origin + Vector3.back * (pairDistance * 0.5f), 0f);
            SampleFighter right = Spawn(origin + Vector3.forward * (pairDistance * 0.5f), 180f);
            Aim(left, right);
            Aim(right, left);
        }

        private SampleFighter Spawn(Vector3 position, float yaw)
        {
            GameObject actor = Instantiate(
                fighterPrefab, position, Quaternion.Euler(0f, yaw, 0f), transform);
            return actor.GetComponent<SampleFighter>();
        }

        private static void Aim(SampleFighter brainOwner, SampleFighter target)
        {
            SampleEnemyBrain brain = brainOwner.GetComponent<SampleEnemyBrain>();
            if (brain != null)
            {
                brain.SetTarget(target);
            }
        }

        private void Update()
        {
            if (!warmedUp && Time.timeSinceLevelLoad >= warmupSeconds)
            {
                warmedUp = true;
                AbilityDiagnostics.TargetQueries.Reset();
                AbilityDiagnostics.EffectApplications.Reset();
                AbilityDiagnostics.Tasks.Reset();
            }
        }

        private void OnGUI()
        {
            style ??= new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true };

            GUILayout.BeginArea(new Rect(12f, 12f, 620f, 200f));
            GUILayout.Label(
                $"<b>{pairCount * 2} fighters</b>   {(warmedUp ? "measuring" : "warming up")}   " +
                $"{1f / Mathf.Max(Time.smoothDeltaTime, 0.0001f):0} fps",
                style);
            Describe(AbilityDiagnostics.TargetQueries);
            Describe(AbilityDiagnostics.EffectApplications);
            Describe(AbilityDiagnostics.Tasks);
            GUILayout.EndArea();
        }

        private void Describe(AbilityDiagnosticCounter counter)
        {
            GUILayout.Label(
                $"{counter.Name}: {counter.Count} calls, " +
                $"avg {counter.AverageMilliseconds:0.000} ms, " +
                $"max {counter.MaxMilliseconds:0.000} ms, " +
                $"{counter.AllocatedBytes / 1024f:0.0} KB allocated",
                style);
        }
    }
}
