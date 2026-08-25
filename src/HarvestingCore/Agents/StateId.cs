namespace HarvestingCore.Agents
{
    /// <summary>
    /// The enumeration of agent states (Glossary: State_Id).
    /// </summary>
    public enum StateId
    {
        Idle,
        Harvest,
        GoToRefuel,
        GoToDump,
        GoToMeetingPoint,
        WaitTractor,
        WaitHarvester,
        Inactive
    }
}
