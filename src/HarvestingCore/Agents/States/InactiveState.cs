namespace HarvestingCore.Agents.States
{
    /// <summary>
    /// Terminal state (Assumption 7). Position and load are frozen; Execute is
    /// a no-op. Behaviour body lands in task 10.
    /// </summary>
    public sealed class InactiveState : AgentState
    {
        public override StateId Id => StateId.Inactive;

        public override void Execute(Agent agent, AgentContext context)
        {
        }
    }
}
