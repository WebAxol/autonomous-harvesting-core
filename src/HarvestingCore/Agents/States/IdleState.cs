namespace HarvestingCore.Agents.States
{
    /// <summary>
    /// Waits for a guard (area assignment or fuel reserve) to fire a transition.
    /// Behaviour body lands in task 10.
    /// </summary>
    public sealed class IdleState : AgentState
    {
        public override StateId Id => StateId.Idle;

        public override void Execute(Agent agent, AgentContext context)
        {
        }
    }
}
