using HarvestingCore.Configuration;
using HarvestingCore.World;
using HarvestingCore.Pathfinding;

namespace HarvestingCore.Agents
{
    /// <summary>
    /// The Agent subtype that receives load from a Harvester and transports
    /// that load to a Dump_Site (Glossary: Tractor). TransitionTable wiring
    /// lands in task 11.
    /// </summary>
    public sealed class Tractor : Agent
    {
        public override AgentRole Role => AgentRole.Tractor;
        protected override TransitionTable TransitionTable => TransitionTables.Tractor;

        /// <summary>Null when unpaired.</summary>
        public string AssignedHarvesterId { get; internal set; }

        internal int NavigationStepCost(Cell cell, SimulationConfig config)
        {
            if (cell.State == CellState.Blocked || cell.State == CellState.Crop)
            {
                return CostField.Unreachable;
            }

            return config.TerrainCost(cell.State) + cell.Popularity;
        }

        public Tractor(string id, GridPosition start, WorldModel model, SimulationConfig config,
            int? maxLoad = null, int? maxFuel = null, int? fuelConsumption = null)
            : base(id, start, model, config, maxLoad, maxFuel, fuelConsumption)
        {
        }
    }
}
