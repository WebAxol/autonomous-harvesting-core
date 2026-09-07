using System.Collections.Generic;
using HarvestingCore.World;
using System;

namespace HarvestingCore.Agents.States
{
    /// <summary>
    /// Plans a path to the nearest dump site, moves, and dumps on arrival
    /// (Req 6.1).
    /// </summary>
    public sealed class GoToDumpState : AgentState
    {
        public override StateId Id => StateId.GoToDump;

        public override void OnEnter(Agent agent, AgentContext context)
        {
            Func<Cell, int> stepCost = null;

            if (agent is Tractor tractor)
            {
                stepCost = cell =>
                    tractor.NavigationStepCost(cell, context.Config);
            }

            if (context.PathFinder.TryCostToNearest(
                agent.Position,
                context.Model.DumpSites,
                out GridPosition nearest,
                out _,
                stepCost))
            {
                var path = context.PathFinder.PathToCell(
                    agent.Position,
                    nearest,
                    stepCostOverride: stepCost);

                agent.SetPath(path);
            }
        }

        public override void Execute(Agent agent, AgentContext context)
        {
            agent.Move(context);

            if (IsAtAnyOf(agent.Position, context.Model.DumpSites))
            {
                agent.DumpLoad(context);
            }
        }

        public override void OnExit(Agent agent, AgentContext context)
        {
            agent.ClearPath();
        }

        private static bool IsAtAnyOf(GridPosition position, IReadOnlyList<GridPosition> positions)
        {
            for (int i = 0; i < positions.Count; i++)
            {
                if (positions[i].Equals(position))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
