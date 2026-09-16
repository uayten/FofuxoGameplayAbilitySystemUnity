using System;
using Unity.Profiling;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// The one switch for everything the package records about itself: the
    /// per-actor <see cref="AbilityEventHistory"/> and the allocation and
    /// timing counters. Off, every record site is a single static bool read
    /// and nothing is allocated, so a shipped game pays for none of it. On,
    /// each actor's history fills and the counters accumulate, and the editor
    /// window reads both.
    ///
    /// Runtime code only ever writes here. Nothing in this class changes
    /// gameplay, and nothing in it touches time: hit-stop and slow motion are
    /// editor tooling that listens to <see cref="EventRecorded"/>, never a
    /// runtime feature.
    /// </summary>
    public static class AbilityDiagnostics
    {
        public const int DefaultHistoryCapacity = 256;

        /// <summary>
        /// Zero-cost in a release player, visible in the Profiler window in
        /// the Editor and development builds, whatever <see cref="Enabled"/>
        /// says. The counters below are the per-site tally the window shows.
        /// </summary>
        internal static readonly ProfilerMarker TargetQueryMarker = new("GAS.TargetQuery");
        internal static readonly ProfilerMarker EffectApplicationMarker = new("GAS.EffectApplication");
        internal static readonly ProfilerMarker TaskMarker = new("GAS.Task");

        private static bool enabled =
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            true;
#else
            false;
#endif

        private static ProfilerRecorder allocationRecorder;
        private static bool allocationRecorderStarted;

        /// <summary>
        /// Master switch. Defaults on in the Editor and in development builds,
        /// off in a release player, and can be flipped at any moment. Turning
        /// it off keeps whatever the histories already hold.
        /// </summary>
        public static bool Enabled
        {
            get => enabled;
            set
            {
                enabled = value;
                if (!value)
                {
                    StopAllocationRecorder();
                }
            }
        }

        /// <summary>
        /// Capacity an actor's history is created with. A history that already
        /// exists keeps the capacity it was born with.
        /// </summary>
        public static int HistoryCapacity { get; set; } = DefaultHistoryCapacity;

        /// <summary>One sample per shared physics acquisition.</summary>
        public static AbilityDiagnosticCounter TargetQueries { get; } = new("Target queries");

        /// <summary>One sample per spec handed to an actor's effect container.</summary>
        public static AbilityDiagnosticCounter EffectApplications { get; } = new("Effect applications");

        /// <summary>One sample per task start and per task tick.</summary>
        public static AbilityDiagnosticCounter Tasks { get; } = new("Task starts and ticks");

        /// <summary>
        /// Fires for every event any actor records, after it is in that actor's
        /// history. The editor's hit-stop hangs on this, and so may a consumer's
        /// combat log. Never fires while diagnostics are off.
        /// </summary>
        public static event Action<AbilitySystem, AbilityEvent> EventRecorded;

        public static void ResetCounters()
        {
            TargetQueries.Reset();
            EffectApplications.Reset();
            Tasks.Reset();
        }

        internal static void Raise(AbilitySystem system, in AbilityEvent recorded)
        {
            EventRecorded?.Invoke(system, recorded);
        }

        /// <summary>
        /// Managed bytes allocated so far this frame, read from the Profiler's
        /// own counter — the only exact figure Unity's Mono exposes. Zero in a
        /// release player, where the counter does not exist; a sample there
        /// still counts and times, it just reports no allocation.
        /// </summary>
        internal static long AllocatedBytesThisFrame()
        {
            if (!allocationRecorderStarted)
            {
                allocationRecorder = ProfilerRecorder.StartNew(
                    ProfilerCategory.Memory, "GC Allocated In Frame");
                allocationRecorderStarted = true;
            }

            return allocationRecorder.Valid ? allocationRecorder.CurrentValue : 0L;
        }

        private static void StopAllocationRecorder()
        {
            if (!allocationRecorderStarted)
            {
                return;
            }

            allocationRecorder.Dispose();
            allocationRecorder = default;
            allocationRecorderStarted = false;
        }
    }
}
