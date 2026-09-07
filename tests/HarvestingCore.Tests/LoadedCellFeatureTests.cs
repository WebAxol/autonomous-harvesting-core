using System;
using HarvestingCore.Agents;
using HarvestingCore.Configuration;
using HarvestingCore.Coordination;
using HarvestingCore.Pathfinding;
using HarvestingCore.World;
using Xunit;

namespace HarvestingCore.Tests
{
    public sealed class LoadedCellFeatureTests
    {
        private static AgentContext CreateContext(
            WorldModel model,
            SimulationConfig config,
            AgentManager manager)
        {
            return new AgentContext(
                model,
                config,
                new PathFinder(model, config),
                manager,
                new PendingMutations(),
                0,
                _ => { });
        }

        [Fact]
        public void Harvester_WhenMaxLoadReached_MarksCellLoaded()
        {
            var emptyPositions = Array.Empty<GridPosition>();

            WorldModel model = WorldModel.Parse(
                "W",
                emptyPositions,
                emptyPositions);

            var config = new SimulationConfig(capacityFactor: 1.0);
            var manager = new AgentManager();

            var harvester = new Harvester(
                "H1",
                new GridPosition(0, 0),
                model,
                config,
                maxLoad: 1);

            manager.Register(harvester);

            AgentContext context =
                CreateContext(model, config, manager);

            bool harvested = harvester.TryHarvest(context);

            Assert.True(harvested);
            Assert.Equal(
                CellState.Loaded,
                model.CellAt(new GridPosition(0, 0)).State);

            Assert.Equal(0, harvester.Load);
        }

        [Fact]
        public void AgentManager_AssignsLoadedCellToIdleTractor()
        {
            var emptyPositions = Array.Empty<GridPosition>();

            WorldModel model = WorldModel.Parse(
                "L..",
                emptyPositions,
                emptyPositions);

            var config = new SimulationConfig();
            var manager = new AgentManager();

            var tractor = new Tractor(
                "T1",
                new GridPosition(2, 0),
                model,
                config);

            manager.Register(tractor);

            AgentContext context =
                CreateContext(model, config, manager);

            manager.AssignLoadedCells(context);

            Assert.True(tractor.AssignedLoadedCell.HasValue);

            Assert.Equal(
                new GridPosition(0, 0),
                tractor.AssignedLoadedCell.Value);

            Assert.True(tractor.MeetingPoint.HasValue);

            Assert.Equal(
                new GridPosition(0, 0),
                tractor.MeetingPoint.Value);
        }

        [Fact]
        public void Tractor_FollowsPathToLoadedCell()
        {
            var emptyPositions = Array.Empty<GridPosition>();

            WorldModel model = WorldModel.Parse(
                "L..",
                emptyPositions,
                emptyPositions);

            var config = new SimulationConfig();
            var manager = new AgentManager();

            var tractor = new Tractor(
                "T1",
                new GridPosition(2, 0),
                model,
                config);

            manager.Register(tractor);

            AgentContext context =
                CreateContext(model, config, manager);

            manager.AssignLoadedCells(context);

            GridPosition target = new GridPosition(0, 0);

            for (int i = 0; i < 10 &&
                 !tractor.Position.Equals(target); i++)
            {
                tractor.Execute(context);
            }

            Assert.Equal(target, tractor.Position);
            Assert.Equal(StateId.Idle, tractor.CurrentState);
        }
    }
}