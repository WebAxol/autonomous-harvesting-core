namespace HarvestingCore.Agents.States
{
    /// <summary>
    /// Plans a path to the negotiated meeting point and moves (Req 8.6, 9.4).
    /// </summary>
    public sealed class GoToMeetingPointState : AgentState
    {
        public override StateId Id => StateId.GoToMeetingPoint;

        public override void OnEnter(Agent agent, AgentContext context)
        {
            if (agent.MeetingPoint.HasValue)
            {
                var path = context.PathFinder.PathToCell(agent.Position, agent.MeetingPoint.Value);
                agent.SetPath(path);
            }
        }

        public override void Execute(Agent agent, AgentContext context)
        {
            agent.Move(context);
        }

        public override void OnExit(Agent agent, AgentContext context)
        {
            agent.ClearPath();
        }
    }
}
