namespace HarvestingCore.Agents.States
{
    /// <summary>
    /// Waits for a guard (area assignment or fuel reserve) to fire a transition.
    /// </summary>
    public sealed class IdleState : AgentState
    {
        public override StateId Id => StateId.Idle;

        public override void OnEnter(Agent agent, AgentContext context)
        {
            agent.ClearPath();
        }

        public override void Execute(Agent agent, AgentContext context)
        {
        }
    }
}
