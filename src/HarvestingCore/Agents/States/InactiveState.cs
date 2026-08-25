namespace HarvestingCore.Agents.States
{
    /// <summary>
    /// Terminal state (Assumption 7). Position and load are frozen; Execute is
    /// a no-op (Req 15.2, 15.3).
    /// </summary>
    public sealed class InactiveState : AgentState
    {
        public override StateId Id => StateId.Inactive;

        public override void OnEnter(Agent agent, AgentContext context)
        {
            agent.ClearPath();
            agent.SetInactiveSinceTick(context.TickIndex);
            context.Pending.EnqueueAssistanceCleanup(agent);
            if (agent.Role == AgentRole.Harvester)
            {
                context.Pending.RequestRedistribution();
            }
        }

        public override void Execute(Agent agent, AgentContext context)
        {
        }
    }
}
