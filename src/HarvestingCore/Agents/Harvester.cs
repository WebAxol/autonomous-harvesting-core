using HarvestingCore.Configuration;
using HarvestingCore.World;

namespace HarvestingCore.Agents
{
    /// <summary>
    /// The Agent subtype that harvests crop cells and requests assistance
    /// (Glossary: Harvester). TransitionTable wiring lands in task 11.
    /// </summary>
    public sealed class Harvester : Agent
    {
        public override AgentRole Role => AgentRole.Harvester;

        public bool AssistanceRequested { get; internal set; }

        public Harvester(string id, GridPosition start, WorldModel model, SimulationConfig config,
            int? maxLoad = null, int? maxFuel = null, int? fuelConsumption = null)
            : base(id, start, model, config, maxLoad, maxFuel, fuelConsumption)
        {
        }

        /// <summary>
        /// Succeeds only on a Crop cell at the harvester position with Load
        /// &lt; MaxLoad; sets the cell to Harvested and raises load by one
        /// (Req 7.1 - 7.3).
        /// </summary>
        public bool TryHarvest(AgentContext context)
        {
            if (Load >= MaxLoad)
            {
                return false;
            }

            Cell cell = context.Model.CellAt(Position);
            if (!cell.Harvest())
            {
                return false;
            }

            SetLoad(Load + 1);
            return true;
        }

        /// <summary>No cell owned by this harvester holds Crop (Req 7.5).</summary>
        public bool IsAreaFinished(AgentContext context)
        {
            var cells = context.Model.Cells;
            for (int i = 0; i < cells.Count; i++)
            {
                Cell cell = cells[i];
                if (cell.IsOwnedBy(Id) && cell.State == CellState.Crop)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>At least one cell owned by this harvester holds Crop (Req 8.3).</summary>
        public bool HasAssignedCrop(AgentContext context)
        {
            var cells = context.Model.Cells;
            for (int i = 0; i < cells.Count; i++)
            {
                Cell cell = cells[i];
                if (cell.IsOwnedBy(Id) && cell.State == CellState.Crop)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
