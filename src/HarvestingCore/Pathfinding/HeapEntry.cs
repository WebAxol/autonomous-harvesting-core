namespace HarvestingCore.Pathfinding
{
    /// <summary>
    /// One entry pushed into a <see cref="DeterministicMinHeap"/>. Sequence is a
    /// monotonically increasing insertion counter that turns equal-priority ties
    /// into a strict total order (Req 13.7).
    /// </summary>
    internal readonly struct HeapEntry
    {
        public int CellIndex { get; }
        public int Priority { get; }   // g for Dijkstra, g + h for A*
        public long Sequence { get; }

        public HeapEntry(int cellIndex, int priority, long sequence)
        {
            CellIndex = cellIndex;
            Priority = priority;
            Sequence = sequence;
        }
    }
}
