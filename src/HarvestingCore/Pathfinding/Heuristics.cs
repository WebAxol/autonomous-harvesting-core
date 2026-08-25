using System;
using HarvestingCore.World;

namespace HarvestingCore.Pathfinding
{
    /// <summary>
    /// Heuristic functions for A*. Zero and Octile are admissible for an
    /// 8-connected grid whose step costs are all >= minCost; SquaredEuclidean
    /// mirrors the reference implementation and is not admissible.
    /// </summary>
    internal static class Heuristics
    {
        public static int Zero(GridPosition a, GridPosition b)
        {
            return 0;
        }

        /// <summary>Octile distance scaled by the cheapest possible step cost (Req 14.7).</summary>
        public static int Octile(GridPosition a, GridPosition b, int minCost)
        {
            int dx = Math.Abs(a.X - b.X);
            int dy = Math.Abs(a.Y - b.Y);
            return minCost * Math.Max(dx, dy);
        }

        /// <summary>Reference behaviour: dx*dx + dy*dy. Fast and greedy, not admissible.</summary>
        public static int SquaredEuclidean(GridPosition a, GridPosition b)
        {
            int dx = a.X - b.X;
            int dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }
    }
}
