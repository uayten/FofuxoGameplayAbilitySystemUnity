using System;
using System.Diagnostics;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// How often one kind of work ran, how long it took and how much it
    /// allocated, accumulated since the last reset. A sample is opened with
    /// <see cref="Begin"/> and closed by disposing it, so the call site is one
    /// <c>using</c> line; while <see cref="AbilityDiagnostics.Enabled"/> is
    /// off the sample is inert and costs a static bool read.
    ///
    /// Samples of one counter may nest — a task that starts another task
    /// inside its own start — and the outer one then includes the inner one.
    /// The count stays exact; the time is a ceiling, never a floor.
    /// </summary>
    public sealed class AbilityDiagnosticCounter
    {
        public AbilityDiagnosticCounter(string name)
        {
            Name = name ?? string.Empty;
        }

        public string Name { get; }

        /// <summary>How many samples closed since the last reset.</summary>
        public long Count { get; private set; }

        /// <summary>Total time inside samples, in <see cref="Stopwatch"/> ticks.</summary>
        public long TotalTicks { get; private set; }

        /// <summary>The longest single sample, in <see cref="Stopwatch"/> ticks.</summary>
        public long MaxTicks { get; private set; }

        /// <summary>
        /// Managed bytes allocated inside samples. Exact where the Profiler's
        /// allocation counter exists — the Editor and development builds — and
        /// zero where it does not.
        /// </summary>
        public long AllocatedBytes { get; private set; }

        public double TotalMilliseconds => ToMilliseconds(TotalTicks);
        public double MaxMilliseconds => ToMilliseconds(MaxTicks);
        public double AverageMilliseconds => Count == 0 ? 0d : TotalMilliseconds / Count;

        /// <summary>Clears the counts and timings, so a measurement can start from now.</summary>
        public void Reset()
        {
            Count = 0;
            TotalTicks = 0;
            MaxTicks = 0;
            AllocatedBytes = 0;
        }

        /// <summary>
        /// Opens a sample. Dispose it to close it; the default sample, returned
        /// while diagnostics are off, does nothing on dispose.
        /// </summary>
        public Sample Begin()
        {
            return AbilityDiagnostics.Enabled
                ? new Sample(
                    this,
                    Stopwatch.GetTimestamp(),
                    AbilityDiagnostics.AllocatedBytesThisFrame())
                : default;
        }

        internal void End(long startTicks, long startBytes)
        {
            long elapsed = Stopwatch.GetTimestamp() - startTicks;
            if (elapsed < 0)
            {
                elapsed = 0;
            }

            Count++;
            TotalTicks += elapsed;
            if (elapsed > MaxTicks)
            {
                MaxTicks = elapsed;
            }

            // The frame counter resets every frame, so a sample that somehow
            // straddled one reads negative; that is the only way it can, and
            // it means "unknown", not "freed".
            long allocated = AbilityDiagnostics.AllocatedBytesThisFrame() - startBytes;
            if (allocated > 0)
            {
                AllocatedBytes += allocated;
            }
        }

        public override string ToString()
        {
            return $"{Name}: {Count} × {AverageMilliseconds:0.000} ms " +
                   $"(max {MaxMilliseconds:0.000} ms, {AllocatedBytes} B)";
        }

        private static double ToMilliseconds(long ticks)
        {
            return ticks * 1000d / Stopwatch.Frequency;
        }

        /// <summary>One open sample. A struct, so opening one allocates nothing.</summary>
        public readonly struct Sample : IDisposable
        {
            private readonly AbilityDiagnosticCounter counter;
            private readonly long startTicks;
            private readonly long startBytes;

            internal Sample(AbilityDiagnosticCounter counter, long startTicks, long startBytes)
            {
                this.counter = counter;
                this.startTicks = startTicks;
                this.startBytes = startBytes;
            }

            /// <summary>
            /// Closes the sample and folds its time and allocation into the
            /// counter.
            /// </summary>
            public void Dispose()
            {
                counter?.End(startTicks, startBytes);
            }
        }
    }
}
