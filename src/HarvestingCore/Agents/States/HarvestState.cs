using HarvestingCore.World;

namespace HarvestingCore.Agents.States
{
    /// <summary>
    /// Harvests the occupied cell, or moves toward the best owned crop cell.
    /// Only ever entered by a Harvester (Req 7.1, 7.4).
    /// </summary>
    public sealed class HarvestState : AgentState
    {
        public override StateId Id => StateId.Harvest;

        public override void Execute(Agent agent, AgentContext context)
        {
            var harvester = (Harvester)agent;
            if (harvester.TryHarvest(context))
            {
                return;
            }

            if (agent.Path.Count == 0)
            {
                var path = context.PathFinder.PathToBestCell(agent.Position, CellState.Crop, agent.Id);
                agent.SetPath(path);
            }

            agent.Move(context);
        }
    }
}
