namespace HarvestingCore.Agents.States
{
    /// <summary>
    /// Waits in place for the assigned harvester; the transfer is resolved by
    /// World after all agents run (Req 16.2).
    /// </summary>
    public sealed class WaitHarvesterState : AgentState
    {
        public override StateId Id => StateId.WaitHarvester;

        public override void OnEnter(Agent agent, AgentContext context)
        {
            agent.ClearPath();
            context.Pending.EnqueueTransferReady(agent);
        }

        public override void Execute(Agent agent, AgentContext context)
        {
        }

        public override void OnExit(Agent agent, AgentContext context)
        {
            agent.ClearTransferCompleted();
        }
    }
}
