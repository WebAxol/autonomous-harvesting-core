using System.Collections.Generic;
using HarvestingCore.World;
using System;

namespace HarvestingCore.Agents.States
{
    /// <summary>
    /// Plans a path to the nearest refuel station, moves, and refuels on
    /// arrival (Req 5.1).
    /// </summary>
    public sealed class GoToRefuelState : AgentState
    {
        public override StateId Id => StateId.GoToRefuel;

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
                context.Model.RefuelStations,
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

            if (IsAtAnyOf(agent.Position, context.Model.RefuelStations))
            {
                agent.Refuel(context);
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
