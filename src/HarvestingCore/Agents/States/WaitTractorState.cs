namespace HarvestingCore.Agents.States
{
    /// <summary>
    /// Waits in place for the assigned tractor; the transfer is resolved by
    /// World after all agents run. Behaviour body lands in task 10.
    /// </summary>
    public sealed class WaitTractorState : AgentState
    {
        public override StateId Id => StateId.WaitTractor;

        public override void Execute(Agent agent, AgentContext context)
        {
        }
    }
}
