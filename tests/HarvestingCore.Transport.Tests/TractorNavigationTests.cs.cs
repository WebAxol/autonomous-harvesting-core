using System;
using System.Linq;
using HarvestingCore.Agents;
using HarvestingCore.Configuration;
using HarvestingCore.Coordination;
using HarvestingCore.Pathfinding;
using HarvestingCore.World;
using Xunit;

namespace HarvestingCore.Transport.Tests
{
    public sealed class TractorNavigationTests
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
        public void Tractor_Move_DoesNotEnterCrop()
        {
            GridPosition[] none = Array.Empty<GridPosition>();

            WorldModel model = WorldModel.Parse(
                ".W",
                none,
                none);

            var config = new SimulationConfig();
            var manager = new AgentManager();

            var tractor = new Tractor(
                "T1",
                new GridPosition(0, 0),
                model,
                config);

            manager.Register(tractor);

            AgentContext context =
                CreateContext(model, config, manager);

            tractor.SetPath(new[]
            {
                new GridPosition(1, 0)
            });

            int fuelBefore = tractor.Fuel;

            tractor.Move(context);

            Assert.Equal(
                new GridPosition(0, 0),
                tractor.Position);

            Assert.True(tractor.PathInvalidatedThisTick);
            Assert.Equal(fuelBefore, tractor.Fuel);

            Assert.Equal(
                0,
                model.CellAt(new GridPosition(1, 0)).Popularity);
        }

        [Fact]
        public void Tractor_GoToRefuel_AvoidsCrop()
        {
            GridPosition[] none = Array.Empty<GridPosition>();

            WorldModel model = WorldModel.Parse(
                "...\n" +
                ".W.\n" +
                "...",
                new[]
                {
                    new GridPosition(2, 1)
                },
                none);

            var config = new SimulationConfig();
            var manager = new AgentManager();

            var tractor = new Tractor(
                "T1",
                new GridPosition(0, 1),
                model,
                config);

            manager.Register(tractor);

            AgentContext context =
                CreateContext(model, config, manager);

            tractor.Transition(
                StateId.GoToRefuel,
                context);

            Assert.DoesNotContain(
                new GridPosition(1, 1),
                tractor.Path);

            Assert.Contains(
                new GridPosition(2, 1),
                tractor.Path);
        }

        [Fact]
        public void Tractor_PrefersLessVisitedRoute()
        {
            GridPosition[] none = Array.Empty<GridPosition>();

            WorldModel model = WorldModel.Parse(
                "...\n" +
                ".W.\n" +
                "...",
                new[]
                {
                    new GridPosition(2, 1)
                },
                none);

            var config = new SimulationConfig();
            var manager = new AgentManager();

            GridPosition upper = new GridPosition(1, 0);
            GridPosition lower = new GridPosition(1, 2);

            for (int i = 0; i < 10; i++)
            {
                model.CellAt(upper).RegisterEntry();
            }

            var tractor = new Tractor(
                "T1",
                new GridPosition(0, 1),
                model,
                config);

            manager.Register(tractor);

            AgentContext context =
                CreateContext(model, config, manager);

            tractor.Transition(
                StateId.GoToRefuel,
                context);

            Assert.DoesNotContain(upper, tractor.Path);
            Assert.Contains(lower, tractor.Path);
        }

        [Fact]
        public void Harvester_CanStillEnterCrop()
        {
            GridPosition[] none = Array.Empty<GridPosition>();

            WorldModel model = WorldModel.Parse(
                ".W",
                none,
                none);

            var config = new SimulationConfig();
            var manager = new AgentManager();

            var harvester = new Harvester(
                "H1",
                new GridPosition(0, 0),
                model,
                config);

            manager.Register(harvester);

            AgentContext context =
                CreateContext(model, config, manager);

            harvester.SetPath(new[]
            {
                new GridPosition(1, 0)
            });

            harvester.Move(context);

            Assert.Equal(
                new GridPosition(1, 0),
                harvester.Position);
        }
    }
}