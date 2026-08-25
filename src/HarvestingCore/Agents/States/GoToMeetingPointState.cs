namespace HarvestingCore.Agents.States
{
    /// <summary>
    /// Plans a path to the negotiated meeting point and moves. Behaviour body
    /// lands in task 10.
    /// </summary>
    public sealed class GoToMeetingPointState : AgentState
    {
        public override StateId Id => StateId.GoToMeetingPoint;

        public override void Execute(Agent agent, AgentContext context)
        {
        }
    }
}
