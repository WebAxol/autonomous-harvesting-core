using System.Collections.Generic;
using HarvestingCore.Agents;
using HarvestingCore.World;

namespace HarvestingCore.Coordination
{
    /// <summary>
    /// Partitions grid cells among active Harvesters using multi-source
    /// breadth-first search (Glossary: Area_Distributor). Direct translation of
    /// reference/algorithms/area_distribution.cpp with an explicit deterministic
    /// seeding order.
    /// </summary>
    public sealed class AreaDistributor
    {
        /// <summary>
        /// Clears every owner first, then seeds all non-INACTIVE harvesters in
        /// registration order (skipping Blocked or already-owned seed cells,
        /// assigning the seed cell to its own harvester), then runs one FIFO BFS
        /// expanding through MoveOrder in sequence and claiming only unowned
        /// non-Blocked cells. With zero active harvesters nothing is seeded and
        /// every owner stays unassigned (Req 12.1 - 12.5, 12.9).
        /// </summary>
        public void Distribute(WorldModel model, IReadOnlyList<Harvester> harvesters)
        {
            IReadOnlyList<Cell> cells = model.Cells;
            for (int i = 0; i < cells.Count; i++)
            {
                cells[i].ClearOwner();
            }

            var queue = new Queue<int>();

            for (int i = 0; i < harvesters.Count; i++)
            {
                Harvester harvester = harvesters[i];
                if (harvester.CurrentState == StateId.Inactive)
                {
                    continue;
                }

                int index = model.IndexOf(harvester.Position);
                Cell seedCell = cells[index];
                if (seedCell.State == CellState.Blocked)
                {
                    continue;
                }
                if (seedCell.OwnerId != Cell.NoOwner)
                {
                    continue;
                }

                seedCell.AssignOwner(harvester.Id);
                queue.Enqueue(index);
            }

            var offsets = MoveOrder.Offsets;
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                string owner = cells[current].OwnerId;
                GridPosition position = model.PositionOf(current);

                for (int i = 0; i < offsets.Length; i++)
                {
                    GridPosition neighbourPosition = position.Offset(offsets[i].Dx, offsets[i].Dy);
                    if (!model.InBounds(neighbourPosition))
                    {
                        continue;
                    }

                    int neighbourIndex = model.IndexOf(neighbourPosition);
                    Cell neighbourCell = cells[neighbourIndex];
                    if (neighbourCell.State == CellState.Blocked)
                    {
                        continue;
                    }
                    if (neighbourCell.OwnerId != Cell.NoOwner)
                    {
                        continue;
                    }

                    neighbourCell.AssignOwner(owner);
                    queue.Enqueue(neighbourIndex);
                }
            }
        }
    }
}
