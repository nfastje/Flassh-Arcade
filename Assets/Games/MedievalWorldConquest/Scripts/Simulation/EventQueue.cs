using System;
using System.Collections.Generic;
using UnityEngine;

namespace MedievalWorldConquest.Simulation
{
    /// <summary>
    /// Events waiting to happen, soonest first (a binary min-heap). Events at the same time come out in the order
    /// they were scheduled, so the simulation is deterministic.
    /// </summary>
    [Serializable]
    public class EventQueue
    {
        [SerializeField] List<ScheduledEvent> heap = new List<ScheduledEvent>();
        [SerializeField] long nextSequence;

        public int Count => heap.Count;

        public ScheduledEvent Peek()
        {
            if (heap.Count == 0) throw new InvalidOperationException("The event queue is empty.");
            return heap[0];
        }

        /// <summary>Adds an event (its <see cref="ScheduledEvent.Sequence"/> is assigned here) and returns it.</summary>
        public ScheduledEvent Push(ScheduledEvent e)
        {
            e.Sequence = nextSequence++;
            heap.Add(e);
            SiftUp(heap.Count - 1);
            return e;
        }

        public ScheduledEvent Pop()
        {
            var top = Peek();
            int last = heap.Count - 1;
            heap[0] = heap[last];
            heap.RemoveAt(last);
            if (heap.Count > 0) SiftDown(0);
            return top;
        }

        /// <summary>The pending events in no particular order, for display or inspection.</summary>
        public IReadOnlyList<ScheduledEvent> Pending => heap;

        static bool Before(in ScheduledEvent a, in ScheduledEvent b) =>
            a.Time < b.Time || (a.Time == b.Time && a.Sequence < b.Sequence);

        void SiftUp(int i)
        {
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (!Before(heap[i], heap[parent])) break;
                (heap[i], heap[parent]) = (heap[parent], heap[i]);
                i = parent;
            }
        }

        void SiftDown(int i)
        {
            while (true)
            {
                int left = i * 2 + 1, right = left + 1, smallest = i;
                if (left < heap.Count && Before(heap[left], heap[smallest])) smallest = left;
                if (right < heap.Count && Before(heap[right], heap[smallest])) smallest = right;
                if (smallest == i) return;
                (heap[i], heap[smallest]) = (heap[smallest], heap[i]);
                i = smallest;
            }
        }
    }
}
