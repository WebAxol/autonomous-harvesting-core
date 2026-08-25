namespace HarvestingCore.Agents.States
{
    /// <summary>
    /// Harvests the occupied cell, or moves toward the best owned crop cell.
    /// Behaviour body lands in task 10.
    /// </summary>
    public sealed class HarvestState : AgentState
    {
        public override StateId Id => StateId.Harvest;

        public override void Execute(Agent agent, AgentContext context)
        {
        }
    }
}
