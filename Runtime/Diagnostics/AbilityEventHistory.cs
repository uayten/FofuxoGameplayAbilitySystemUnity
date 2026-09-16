using System;
using System.Collections.Generic;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// The last N things that happened to one actor, oldest first, in a ring
    /// that never grows after it is created. Recording into a full ring drops
    /// the oldest entry; <see cref="TotalRecorded"/> keeps counting, so a
    /// reader can tell how much it missed.
    ///
    /// Owned by <see cref="AbilitySystem.History"/> and created on the first
    /// record, so an actor that never records — diagnostics off — never
    /// allocates one.
    /// </summary>
    public sealed class AbilityEventHistory
    {
        private readonly AbilityEvent[] ring;
        private int start;

        public AbilityEventHistory(int capacity)
        {
            ring = new AbilityEvent[Math.Max(1, capacity)];
        }

        public int Capacity => ring.Length;

        /// <summary>How many entries the ring holds right now, at most <see cref="Capacity"/>.</summary>
        public int Count { get; private set; }

        /// <summary>Every event ever recorded here, dropped ones included.</summary>
        public long TotalRecorded { get; private set; }

        /// <summary>How many events the ring has already forgotten.</summary>
        public long DroppedCount => TotalRecorded - Count;

        /// <summary>Oldest first: index 0 is the earliest entry still held.</summary>
        public AbilityEvent this[int index]
        {
            get
            {
                if (index < 0 || index >= Count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                return ring[(start + index) % ring.Length];
            }
        }

        /// <summary>The most recent entry. Undefined while <see cref="Count"/> is zero.</summary>
        public AbilityEvent Latest => Count == 0 ? default : this[Count - 1];

        /// <summary>Fires after an entry lands in the ring.</summary>
        public event Action<AbilityEvent> Recorded;

        public void Record(in AbilityEvent recorded)
        {
            int slot = (start + Count) % ring.Length;
            if (Count == ring.Length)
            {
                start = (start + 1) % ring.Length;
            }
            else
            {
                Count++;
            }

            ring[slot] = recorded;
            TotalRecorded++;
            Recorded?.Invoke(recorded);
        }

        public void Clear()
        {
            Array.Clear(ring, 0, ring.Length);
            start = 0;
            Count = 0;
        }

        /// <summary>
        /// Appends the held entries to a list, for a reader that wants to sort
        /// or filter them without holding the ring.
        /// </summary>
        /// <returns>How many were appended.</returns>
        public int CopyTo(List<AbilityEvent> into, bool newestFirst = false)
        {
            if (into == null)
            {
                return 0;
            }

            if (newestFirst)
            {
                for (int i = Count - 1; i >= 0; i--)
                {
                    into.Add(this[i]);
                }
            }
            else
            {
                for (int i = 0; i < Count; i++)
                {
                    into.Add(this[i]);
                }
            }

            return Count;
        }
    }
}
