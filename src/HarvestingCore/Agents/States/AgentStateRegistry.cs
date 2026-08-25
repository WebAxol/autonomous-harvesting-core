using System;
using System.Collections.Generic;

namespace HarvestingCore.Agents.States
{
    /// <summary>
    /// Holds exactly one immutable singleton instance per StateId. No per-agent
    /// allocation, no hidden per-state mutable data, so nothing can drift
    /// between two identically-seeded runs.
    /// </summary>
    public static class AgentStateRegistry
    {
        private static readonly Dictionary<StateId, AgentState> States = new Dictionary<StateId, AgentState>
        {
            { StateId.Idle, new IdleState() },
            { StateId.Harvest, new HarvestState() },
            { StateId.GoToRefuel, new GoToRefuelState() },
            { StateId.GoToDump, new GoToDumpState() },
            { StateId.GoToMeetingPoint, new GoToMeetingPointState() },
            { StateId.WaitTractor, new WaitTractorState() },
            { StateId.WaitHarvester, new WaitHarvesterState() },
            { StateId.Inactive, new InactiveState() }
        };

        public static AgentState Get(StateId id)
        {
            if (!States.TryGetValue(id, out AgentState state))
            {
                throw new ArgumentOutOfRangeException(nameof(id), "Unknown StateId: " + id);
            }
            return state;
        }
    }
}
