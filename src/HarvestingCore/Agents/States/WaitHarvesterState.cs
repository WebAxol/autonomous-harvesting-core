namespace HarvestingCore.Agents.States
{
    /// <summary>
    /// Waits in place for the assigned harvester; the transfer is resolved by
    /// World after all agents run. Behaviour body lands in task 10.
    /// </summary>
    public sealed class WaitHarvesterState : AgentState
    {
        public override StateId Id => StateId.WaitHarvester;

        public override void Execute(Agent agent, AgentContext context)
        {
        }
    }
}
