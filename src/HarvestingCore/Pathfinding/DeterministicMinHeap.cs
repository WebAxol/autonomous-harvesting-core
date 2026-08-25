using System;

namespace HarvestingCore.Pathfinding
{
    /// <summary>
    /// Array-backed binary min-heap over a strict total order, so popping ties on
    /// priority always yields the entry with the lowest insertion sequence number
    /// (Req 13.7). Lazy deletion is used: a cell may be pushed several times, and
    /// callers must skip pops for cells already finalised.
    /// </summary>
    internal sealed class DeterministicMinHeap
    {
        private HeapEntry[] _items;
        private int _count;
        private long _sequence;

        public DeterministicMinHeap(int initialCapacity = 16)
        {
            _items = new HeapEntry[Math.Max(1, initialCapacity)];
            _count = 0;
            _sequence = 0;
        }

        public int Count => _count;

        public void Push(int cellIndex, int priority)
        {
            if (_count == _items.Length)
            {
                Array.Resize(ref _items, _items.Length * 2);
            }

            var entry = new HeapEntry(cellIndex, priority, _sequence++);
            _items[_count] = entry;
            SiftUp(_count);
            _count++;
        }

        public HeapEntry Pop()
        {
            if (_count == 0)
            {
                throw new InvalidOperationException("Cannot pop from an empty heap.");
            }

            HeapEntry root = _items[0];
            _count--;
            _items[0] = _items[_count];
            _items[_count] = default;
            if (_count > 0)
            {
                SiftDown(0);
            }
            return root;
        }

        /// <summary>Reuses the backing array; only resets the logical count and sequence.</summary>
        public void Clear()
        {
            _count = 0;
            _sequence = 0;
        }

        // Strict total order: a < b  <=>  a.Priority < b.Priority
        //                             || (a.Priority == b.Priority && a.Sequence < b.Sequence)
        private static bool Less(in HeapEntry a, in HeapEntry b)
        {
            return a.Priority < b.Priority || (a.Priority == b.Priority && a.Sequence < b.Sequence);
        }

        private void SiftUp(int index)
        {
            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (!Less(_items[index], _items[parent]))
                {
                    break;
                }
                Swap(index, parent);
                index = parent;
            }
        }

        private void SiftDown(int index)
        {
            while (true)
            {
                int left = index * 2 + 1;
                int right = left + 1;
                int smallest = index;

                if (left < _count && Less(_items[left], _items[smallest]))
                {
                    smallest = left;
                }
                if (right < _count && Less(_items[right], _items[smallest]))
                {
                    smallest = right;
                }
                if (smallest == index)
                {
                    break;
                }

                Swap(index, smallest);
                index = smallest;
            }
        }

        private void Swap(int a, int b)
        {
            HeapEntry tmp = _items[a];
            _items[a] = _items[b];
            _items[b] = tmp;
        }
    }
}
