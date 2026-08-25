namespace HarvestingCore.Agents.States
{
    /// <summary>
    /// Waits in place for the assigned tractor; the transfer is resolved by
    /// World after all agents run (Req 16.2).
    /// </summary>
    public sealed class WaitTractorState : AgentState
    {
        public override StateId Id => StateId.WaitTractor;

        public override void OnEnter(Agent agent, AgentContext context)
        {
            agent.ClearPath();
            context.Pending.EnqueueTransferReady(agent);
        }

        public override void Execute(Agent agent, AgentContext context)
        {
        }
    }
}
