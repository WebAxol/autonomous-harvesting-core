using System;
using System.Collections.Generic;
using HarvestingCore.World;

namespace HarvestingCore.Pathfinding
{
    /// <summary>
    /// A full Dijkstra cost field computed once per origin. Coordination scans this
    /// rather than running one search per candidate (tractor selection, meeting
    /// point negotiation).
    /// </summary>
    public sealed class CostField
    {
        public const int Unreachable = int.MaxValue;

        private readonly int[] _costs;
        private readonly int[] _predecessors;   // -1 = none

        public int Width { get; }
        public int Height { get; }
        public GridPosition Origin { get; }
        public IReadOnlyList<int> Costs { get; }

        internal CostField(int width, int height, GridPosition origin, int[] costs, int[] predecessors)
        {
            Width = width;
            Height = height;
            Origin = origin;
            _costs = costs;
            _predecessors = predecessors;
            Costs = Array.AsReadOnly(_costs);
        }

        public bool IsReachable(int index)
        {
            return _costs[index] != Unreachable;
        }

        public int CostAt(int index)
        {
            return _costs[index];
        }

        internal int[] MutableCosts => _costs;
        internal int[] Predecessors => _predecessors;
    }
}
