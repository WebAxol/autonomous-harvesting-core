using System.Collections.Generic;
using HarvestingCore.Pathfinding;
using HarvestingCore.World;

namespace HarvestingCore.Agents
{
    /// <summary>
    /// The two role-specific, priority-ordered transition tables (Req 8.13, 9.11).
    /// Guards are pure predicates that never mutate agent or world state; the only
    /// state they read besides the agent itself is the per-tick flags
    /// (RefuelledThisTick, DumpedThisTick, TransferCompletedThisTick) and the
    /// read-only projections exposed through AgentContext.
    /// </summary>
    public static class TransitionTables
    {
        public static readonly TransitionTable Harvester = new TransitionTable(new[]
        {
            // 1. HARVEST -> GO_TO_REFUEL: fuel at or below the reserve threshold.
            new TransitionRule(StateId.Harvest, StateId.GoToRefuel,
                (agent, ctx) => ctx.Model.RefuelStations.Count > 0
                    && agent.TryEstimateFuelReserve(ctx, out int reserve)
                    && agent.Fuel <= reserve * ctx.Config.HarvesterFuelReserveMultiplier,
                "8.4"),

            // 2. HARVEST -> WAIT_TRACTOR: full load, wait in place.
            new TransitionRule(StateId.Harvest, StateId.WaitTractor,
                (agent, ctx) => agent.Load == agent.MaxLoad,
                "8.8"),

            // 3. HARVEST -> GO_TO_MEETING_POINT: paired tractor with a rendezvous
            //    that is not the harvester's own position (row 2 already handles
            //    the full-load, meet-in-place case).
            new TransitionRule(StateId.Harvest, StateId.GoToMeetingPoint,
                (agent, ctx) => ctx.Manager.TryGetPartner(agent, out _)
                    && agent.MeetingPoint.HasValue
                    && !agent.MeetingPoint.Value.Equals(agent.Position),
                "8.6"),

            // 4. HARVEST -> GO_TO_DUMP: carrying load and the dump is economically
            //    preferable to the nearest available tractor.
            new TransitionRule(StateId.Harvest, StateId.GoToDump,
                (agent, ctx) =>
                {
                    if (agent.Load <= 0 || ctx.Model.DumpSites.Count == 0)
                    {
                        return false;
                    }
                    if (!ctx.PathFinder.TryCostToNearest(agent.Position, ctx.Model.DumpSites, out _, out int dumpCost))
                    {
                        return false;
                    }
                    int tractorCost = CostToNearestAvailableTractor(agent, ctx);
                    if (tractorCost == CostField.Unreachable)
                    {
                        return true;
                    }
                    return dumpCost < tractorCost * ctx.Config.DumpPreferenceFactor;
                },
                "8.10"),

            // 5. HARVEST -> IDLE: nothing left to harvest in the owned area.
            new TransitionRule(StateId.Harvest, StateId.Idle,
                (agent, ctx) => ((Harvester)agent).IsAreaFinished(ctx),
                "8.2"),

            // 6. IDLE -> HARVEST: a crop cell is owned again.
            new TransitionRule(StateId.Idle, StateId.Harvest,
                (agent, ctx) => ((Harvester)agent).HasAssignedCrop(ctx),
                "8.3"),

            // 7. IDLE -> GO_TO_REFUEL: same fuel-reserve guard as row 1, evaluated
            //    from IDLE.
            new TransitionRule(StateId.Idle, StateId.GoToRefuel,
                (agent, ctx) => ctx.Model.RefuelStations.Count > 0
                    && agent.TryEstimateFuelReserve(ctx, out int reserve)
                    && agent.Fuel <= reserve * ctx.Config.HarvesterFuelReserveMultiplier,
                "8.4"),

            // 8. GO_TO_REFUEL -> HARVEST: refuel completed this tick.
            new TransitionRule(StateId.GoToRefuel, StateId.Harvest,
                (agent, ctx) => agent.RefuelledThisTick,
                "8.5"),

            // 9. GO_TO_MEETING_POINT -> WAIT_TRACTOR: arrived at the rendezvous.
            new TransitionRule(StateId.GoToMeetingPoint, StateId.WaitTractor,
                (agent, ctx) => agent.MeetingPoint.HasValue && agent.Position.Equals(agent.MeetingPoint.Value),
                "8.7"),

            // 10. GO_TO_MEETING_POINT -> IDLE: pair lost or the meeting point is
            //     unreachable (path exhausted without arriving).
            new TransitionRule(StateId.GoToMeetingPoint, StateId.Idle,
                (agent, ctx) => agent.Path.Count == 0
                    && (!agent.MeetingPoint.HasValue || !agent.Position.Equals(agent.MeetingPoint.Value)),
                "11.3"),

            // 11. WAIT_TRACTOR -> HARVEST: load transferred to the tractor.
            new TransitionRule(StateId.WaitTractor, StateId.Harvest,
                (agent, ctx) => agent.TransferCompletedThisTick,
                "8.9"),

            // 12. WAIT_TRACTOR -> IDLE: partner went inactive, no pair remains.
            new TransitionRule(StateId.WaitTractor, StateId.Idle,
                (agent, ctx) => !ctx.Manager.IsPaired(agent),
                "10.7"),

            // 13. GO_TO_DUMP -> HARVEST: dump completed this tick.
            new TransitionRule(StateId.GoToDump, StateId.Harvest,
                (agent, ctx) => agent.DumpedThisTick,
                "8.11"),
        });

        public static readonly TransitionTable Tractor = new TransitionTable(new[]
        {
            // 1. IDLE -> GO_TO_REFUEL: fuel at or below the reserve threshold.
            new TransitionRule(StateId.Idle, StateId.GoToRefuel,
                (agent, ctx) => ctx.Model.RefuelStations.Count > 0
                    && agent.TryEstimateFuelReserve(ctx, out int reserve)
                    && agent.Fuel <= reserve * ctx.Config.TractorFuelReserveMultiplier,
                "9.2"),

            // 2. IDLE -> GO_TO_MEETING_POINT: a harvester assignment is in place.
            new TransitionRule(StateId.Idle, StateId.GoToMeetingPoint,
                (agent, ctx) => ((Tractor)agent).AssignedHarvesterId != null && agent.MeetingPoint.HasValue,
                "9.4"),

            // 3. IDLE -> GO_TO_DUMP: still carrying enough load to warrant a dump run.
            new TransitionRule(StateId.Idle, StateId.GoToDump,
                (agent, ctx) => agent.Load > 0 && ctx.Model.DumpSites.Count > 0
                    && agent.Load >= agent.MaxLoad * ctx.Config.CapacityFactor,
                "9.7"),

            // 4. GO_TO_REFUEL -> IDLE: refuel completed this tick.
            new TransitionRule(StateId.GoToRefuel, StateId.Idle,
                (agent, ctx) => agent.RefuelledThisTick,
                "9.3"),

            // 5. GO_TO_MEETING_POINT -> WAIT_HARVESTER: arrived at the rendezvous.
            new TransitionRule(StateId.GoToMeetingPoint, StateId.WaitHarvester,
                (agent, ctx) => agent.MeetingPoint.HasValue && agent.Position.Equals(agent.MeetingPoint.Value),
                "9.5"),

            // 6. GO_TO_MEETING_POINT -> IDLE: partner lost mid-transit.
            new TransitionRule(StateId.GoToMeetingPoint, StateId.Idle,
                (agent, ctx) => ((Tractor)agent).AssignedHarvesterId == null,
                "10.7"),

            // 7. WAIT_HARVESTER -> GO_TO_DUMP: transfer completed and the load
            //    reached the capacity threshold.
            new TransitionRule(StateId.WaitHarvester, StateId.GoToDump,
                (agent, ctx) => agent.TransferCompletedThisTick && ctx.Model.DumpSites.Count > 0
                    && agent.Load >= agent.MaxLoad * ctx.Config.CapacityFactor,
                "9.7"),

            // 8. WAIT_HARVESTER -> IDLE: transfer completed below the capacity
            //    threshold (else-branch of row 7).
            new TransitionRule(StateId.WaitHarvester, StateId.Idle,
                (agent, ctx) => agent.TransferCompletedThisTick,
                "9.6"),

            // 9. WAIT_HARVESTER -> IDLE: partner went inactive, no pair remains.
            new TransitionRule(StateId.WaitHarvester, StateId.Idle,
                (agent, ctx) => ((Tractor)agent).AssignedHarvesterId == null,
                "10.7"),

            // 10. GO_TO_DUMP -> IDLE: dump completed this tick.
            new TransitionRule(StateId.GoToDump, StateId.Idle,
                (agent, ctx) => agent.DumpedThisTick,
                "9.8"),
        });

        /// <summary>Cost from the harvester's position to the nearest tractor that is
        /// IDLE, unpaired, and reachable; CostField.Unreachable when none qualifies.
        /// Pure: allocates a scratch list and reads the manager's registration-ordered
        /// tractor collection, never mutates agent or world state.</summary>
        private static int CostToNearestAvailableTractor(Agent harvester, AgentContext ctx)
        {
            IReadOnlyList<Tractor> tractors = ctx.Manager.Tractors;
            if (tractors.Count == 0)
            {
                return CostField.Unreachable;
            }

            var candidates = new List<GridPosition>();
            for (int i = 0; i < tractors.Count; i++)
            {
                Tractor tractor = tractors[i];
                if (tractor.CurrentState != StateId.Idle)
                {
                    continue;
                }
                if (ctx.Manager.IsPaired(tractor))
                {
                    continue;
                }
                candidates.Add(tractor.Position);
            }

            if (candidates.Count == 0)
            {
                return CostField.Unreachable;
            }

            if (!ctx.PathFinder.TryCostToNearest(harvester.Position, candidates, out _, out int cost))
            {
                return CostField.Unreachable;
            }
            return cost;
        }
    }
}
