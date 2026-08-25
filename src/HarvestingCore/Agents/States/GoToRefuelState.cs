namespace HarvestingCore.Agents.States
{
    /// <summary>
    /// Plans a path to the nearest refuel station, moves, and refuels on
    /// arrival. Behaviour body lands in task 10.
    /// </summary>
    public sealed class GoToRefuelState : AgentState
    {
        public override StateId Id => StateId.GoToRefuel;

        public override void Execute(Agent agent, AgentContext context)
        {
        }
    }
}
