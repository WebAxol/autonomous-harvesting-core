using System;
using System.Collections.Generic;
using HarvestingCore.Configuration;
using HarvestingCore.World;

namespace HarvestingCore.Pathfinding
{
    /// <summary>
    /// Computes paths over a WorldModel grid: a uniform-cost search to the nearest
    /// cell holding a target state (PathToBestCell), an A* search to a specific
    /// cell (PathToCell), and full cost fields used by coordination.
    ///
    /// Scratch state (heap, costs, predecessors, closed set) is reused across calls
    /// and version-stamped rather than cleared, so repeated searches stay O(1) to
    /// reset regardless of grid size.
    /// </summary>
    public sealed class PathFinder
    {
        private readonly WorldModel _model;
        private readonly SimulationConfig _config;
        private readonly int _size;

        private readonly DeterministicMinHeap _heap;
        private readonly int[] _costs;
        private readonly int[] _predecessors;      // -1 = none
        private readonly int[] _costStamp;
        private readonly int[] _closedStamp;
        private int _version;

        public PathFinder(WorldModel model, SimulationConfig config)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _size = model.Width * model.Height;

            _heap = new DeterministicMinHeap(_size);
            _costs = new int[_size];
            _predecessors = new int[_size];
            _costStamp = new int[_size];
            _closedStamp = new int[_size];
            _version = 0;

            for (int i = 0; i < _size; i++)
            {
                _predecessors[i] = -1;
                _costStamp[i] = -1;
                _closedStamp[i] = -1;
            }
        }

        /// <summary>
        /// Dijkstra to the nearest cell holding targetState; ownerFilter == null
        /// disables the ownership check (Req 13.6). Terminates on the first popped
        /// cell that satisfies both, so the returned target is provably cheapest.
        /// </summary>
        public IReadOnlyList<GridPosition> PathToBestCell(
            GridPosition origin, CellState targetState, string ownerFilter = null)
        {
            if (!_model.InBounds(origin))
            {
                return Array.Empty<GridPosition>();
            }

            int foundIndex = RunSearch(origin, index =>
            {
                Cell cell = _model.Cells[index];
                if (cell.State != targetState)
                {
                    return false;
                }
                return ownerFilter == null || cell.IsOwnedBy(ownerFilter);
            }, null);

            if (foundIndex == -1)
            {
                return Array.Empty<GridPosition>();
            }
            return Reconstruct(foundIndex);
        }

        /// <summary>
        /// A* to one cell using config.Heuristic (Req 14.1) or an explicit override.
        /// Short-circuits origin == target to a single-element path (Req 14.5) and
        /// returns an empty path for out-of-bounds, Blocked, or unreachable targets.
        /// </summary>
        public IReadOnlyList<GridPosition> PathToCell(
            GridPosition origin, GridPosition target, HeuristicKind? heuristicOverride = null)
        {
            if (!_model.InBounds(origin) || !_model.InBounds(target))
            {
                return Array.Empty<GridPosition>();
            }

            if (origin.Equals(target))
            {
                return Array.Empty<GridPosition>();
            }

            int targetIndex = _model.IndexOf(target);
            if (_model.Cells[targetIndex].State == CellState.Blocked)
            {
                return Array.Empty<GridPosition>();
            }

            Func<GridPosition, int> heuristic = BuildHeuristic(heuristicOverride ?? _config.Heuristic, target);
            int foundIndex = RunSearch(origin, index => index == targetIndex, heuristic);

            if (foundIndex == -1)
            {
                return Array.Empty<GridPosition>();
            }
            return Reconstruct(foundIndex);
        }

        /// <summary>Full Dijkstra cost field, no early termination.</summary>
        public CostField ComputeCostField(GridPosition origin)
        {
            if (!_model.InBounds(origin))
            {
                throw new ArgumentOutOfRangeException(nameof(origin), "origin is out of bounds.");
            }

            RunSearch(origin, index => false, null);

            return new CostField(_model.Width, _model.Height, origin, SnapshotCosts(), SnapshotPredecessors());
        }

        /// <summary>Cost of the cheapest path to the nearest member of targets.</summary>
        public bool TryCostToNearest(GridPosition origin, IReadOnlyList<GridPosition> targets,
            out GridPosition best, out int cost)
        {
            best = default;
            cost = CostField.Unreachable;

            if (targets == null || targets.Count == 0)
            {
                return false;
            }

            CostField field = ComputeCostField(origin);
            bool found = false;

            for (int i = 0; i < targets.Count; i++)
            {
                GridPosition candidate = targets[i];
                if (!_model.InBounds(candidate))
                {
                    continue;
                }

                int index = _model.IndexOf(candidate);
                if (!field.IsReachable(index))
                {
                    continue;
                }

                int candidateCost = field.CostAt(index);
                if (!found || candidateCost < cost)
                {
                    found = true;
                    cost = candidateCost;
                    best = candidate;
                }
            }

            return found;
        }

        /// <summary>Terrain cost of entering position; the Unreachable sentinel for Blocked.</summary>
        internal int StepCostInto(GridPosition position)
        {
            Cell cell = _model.CellAt(position);
            if (cell.State == CellState.Blocked)
            {
                return CostField.Unreachable;
            }
            return _config.TerrainCost(cell.State);
        }

        /// <summary>
        /// Shared search loop: pop cheapest, skip stale closed entries, test the
        /// termination predicate on pop, expand neighbours in MoveOrder sequence
        /// skipping out-of-bounds and Blocked, relax on strict improvement. Returns
        /// the terminal cell index, or -1 when the heap empties without matching.
        /// </summary>
        private int RunSearch(GridPosition origin, Func<int, bool> isTerminal, Func<GridPosition, int> heuristic)
        {
            _version++;
            _heap.Clear();

            int originIndex = _model.IndexOf(origin);
            SetCost(originIndex, 0);
            _predecessors[originIndex] = -1;
            _heap.Push(originIndex, heuristic == null ? 0 : heuristic(origin));

            while (_heap.Count > 0)
            {
                HeapEntry entry = _heap.Pop();
                int index = entry.CellIndex;

                if (IsClosed(index))
                {
                    continue;   // stale entry superseded by a cheaper pop already processed
                }

                if (isTerminal(index))
                {
                    return index;
                }

                SetClosed(index);

                int currentCost = CostOf(index);
                if (currentCost == CostField.Unreachable)
                {
                    continue;   // never relax from a sentinel cost
                }

                GridPosition position = _model.PositionOf(index);
                var offsets = MoveOrder.Offsets;
                for (int i = 0; i < offsets.Length; i++)
                {
                    GridPosition neighbourPosition = position.Offset(offsets[i].Dx, offsets[i].Dy);
                    if (!_model.InBounds(neighbourPosition))
                    {
                        continue;
                    }

                    int neighbourIndex = _model.IndexOf(neighbourPosition);
                    if (IsClosed(neighbourIndex))
                    {
                        continue;
                    }

                    int stepCost = StepCostInto(neighbourPosition);
                    if (stepCost == CostField.Unreachable)
                    {
                        continue;   // Blocked
                    }

                    int candidateCost = currentCost + stepCost;
                    int existingCost = CostOf(neighbourIndex);

                    if (candidateCost < existingCost)
                    {
                        SetCost(neighbourIndex, candidateCost);
                        _predecessors[neighbourIndex] = index;
                        int priority = candidateCost + (heuristic == null ? 0 : heuristic(neighbourPosition));
                        _heap.Push(neighbourIndex, priority);
                    }
                }
            }

            return -1;
        }

        /// <summary>Walks the predecessor chain from targetIndex and reverses it, so
        /// element 0 is the first step away from the origin of the most recent RunSearch call.</summary>
        private List<GridPosition> Reconstruct(int targetIndex)
        {
            var result = new List<GridPosition>();
            int current = targetIndex;
            while (current != -1)
            {
                result.Add(_model.PositionOf(current));
                current = _predecessors[current];
            }
            result.Reverse();
            // Drop the origin (index 0) — the agent is already there.
            if (result.Count > 0)
            {
                result.RemoveAt(0);
            }
            return result;
        }

        private Func<GridPosition, int> BuildHeuristic(HeuristicKind kind, GridPosition target)
        {
            switch (kind)
            {
                case HeuristicKind.Zero:
                    return position => Heuristics.Zero(position, target);
                case HeuristicKind.SquaredEuclidean:
                    return position => Heuristics.SquaredEuclidean(position, target);
                case HeuristicKind.Octile:
                default:
                    int minCost = _config.MinimumTerrainCost;
                    return position => Heuristics.Octile(position, target, minCost);
            }
        }

        private int[] SnapshotCosts()
        {
            var snapshot = new int[_size];
            for (int i = 0; i < _size; i++)
            {
                snapshot[i] = _costStamp[i] == _version ? _costs[i] : CostField.Unreachable;
            }
            return snapshot;
        }

        private int[] SnapshotPredecessors()
        {
            var snapshot = new int[_size];
            for (int i = 0; i < _size; i++)
            {
                snapshot[i] = _costStamp[i] == _version ? _predecessors[i] : -1;
            }
            return snapshot;
        }

        private int CostOf(int index)
        {
            return _costStamp[index] == _version ? _costs[index] : CostField.Unreachable;
        }

        private bool IsClosed(int index)
        {
            return _closedStamp[index] == _version;
        }

        private void SetCost(int index, int cost)
        {
            _costs[index] = cost;
            _costStamp[index] = _version;
        }

        private void SetClosed(int index)
        {
            _closedStamp[index] = _version;
        }
    }
}
