namespace HarvestingCore.Agents.States
{
    /// <summary>
    /// Plans a path to the nearest dump site, moves, and dumps on arrival.
    /// Behaviour body lands in task 10.
    /// </summary>
    public sealed class GoToDumpState : AgentState
    {
        public override StateId Id => StateId.GoToDump;

        public override void Execute(Agent agent, AgentContext context)
        {
        }
    }
}
