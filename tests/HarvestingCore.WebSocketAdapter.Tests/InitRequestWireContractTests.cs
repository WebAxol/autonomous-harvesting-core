using System.Collections.Generic;
using System.Text.Json;
using AgroAgents.SimulationPort;
using AgroAgents.WebSocketAdapter;
using HarvestingCore.Transport.Dto;
using Xunit;

namespace HarvestingCore.WebSocketAdapter.Tests
{
    /// <summary>
    /// Proves the client-authored init_request payload deserializes cleanly into the
    /// server's <see cref="InitRequest"/> wire shape. This is the contract that lets
    /// the client be the sole author of the world.
    /// </summary>
    public class InitRequestWireContractTests
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private static SessionRequest BuildSampleRequest(string? authoredGridText = null)
        {
            var agents = new List<PortAgentSpec>
            {
                new PortAgentSpec("H1", PortAgentRole.Harvester, new PortGridPosition(0, 0), 100, 1000, 1),
                new PortAgentSpec("T1", PortAgentRole.Tractor, new PortGridPosition(9, 0), null, null, null),
            };

            return new SessionRequest(
                width: 30,
                height: 20,
                seed: 12345,
                cropDensity: 0.55,
                blockedDensity: 0.10,
                authoredGridText: authoredGridText,
                refuelStations: new List<PortGridPosition> { new PortGridPosition(0, 0) },
                dumpSites: new List<PortGridPosition> { new PortGridPosition(9, 0) },
                agents: agents,
                cropCost: 1,
                emptyCost: 2,
                harvestedCost: 10,
                heuristicKind: 1,
                defaultMaxLoad: 100,
                defaultMaxFuel: 1000,
                defaultFuelConsumption: 1,
                dumpPreferenceFactor: 1.0,
                capacityFactor: 0.5,
                harvesterFuelReserveMultiplier: 1.2,
                tractorFuelReserveMultiplier: 2.5);
        }

        [Fact]
        public void Serialize_ProducesInitRequestType()
        {
            string json = InitRequestSerializer.Serialize(BuildSampleRequest());

            var init = JsonSerializer.Deserialize<InitRequest>(json, Options);

            Assert.NotNull(init);
            Assert.Equal("init_request", init!.Type);
        }

        [Fact]
        public void Serialize_RoundTripsScalarFields()
        {
            var request = BuildSampleRequest();
            string json = InitRequestSerializer.Serialize(request);

            var init = JsonSerializer.Deserialize<InitRequest>(json, Options)!;

            Assert.Equal(request.Width, init.Width);
            Assert.Equal(request.Height, init.Height);
            Assert.Equal(request.Seed, init.Seed);
            Assert.Equal(request.CropDensity, init.CropDensity);
            Assert.Equal(request.BlockedDensity, init.BlockedDensity);
            Assert.Equal(request.CropCost, init.CropCost);
            Assert.Equal(request.EmptyCost, init.EmptyCost);
            Assert.Equal(request.HarvestedCost, init.HarvestedCost);
            Assert.Equal(request.HeuristicKind, init.HeuristicKind);
            Assert.Equal(request.DefaultMaxLoad, init.DefaultMaxLoad);
            Assert.Equal(request.DefaultMaxFuel, init.DefaultMaxFuel);
            Assert.Equal(request.DefaultFuelConsumption, init.DefaultFuelConsumption);
            Assert.Equal(request.DumpPreferenceFactor, init.DumpPreferenceFactor);
            Assert.Equal(request.CapacityFactor, init.CapacityFactor);
            Assert.Equal(request.HarvesterFuelReserveMultiplier, init.HarvesterFuelReserveMultiplier);
            Assert.Equal(request.TractorFuelReserveMultiplier, init.TractorFuelReserveMultiplier);
        }

        [Fact]
        public void Serialize_RoundTripsAgentsStationsAndDumps()
        {
            var request = BuildSampleRequest();
            string json = InitRequestSerializer.Serialize(request);

            var init = JsonSerializer.Deserialize<InitRequest>(json, Options)!;

            Assert.Equal(2, init.Agents.Count);

            AgentSpecDto h1 = init.Agents.Find(a => a.Id == "H1")!;
            Assert.Equal("Harvester", h1.Role);
            Assert.Equal(0, h1.X);
            Assert.Equal(0, h1.Y);
            Assert.Equal(100, h1.MaxLoad);

            AgentSpecDto t1 = init.Agents.Find(a => a.Id == "T1")!;
            Assert.Equal("Tractor", t1.Role);
            Assert.Null(t1.MaxLoad);
            Assert.Null(t1.FuelConsumption);

            Assert.Single(init.RefuelStations);
            Assert.Equal(0, init.RefuelStations[0].X);
            Assert.Single(init.DumpSites);
            Assert.Equal(9, init.DumpSites[0].X);
        }

        [Fact]
        public void Serialize_NullAuthoredGrid_DeserializesAsNull()
        {
            string json = InitRequestSerializer.Serialize(BuildSampleRequest(authoredGridText: null));
            var init = JsonSerializer.Deserialize<InitRequest>(json, Options)!;
            Assert.Null(init.AuthoredGridText);
        }

        [Fact]
        public void Serialize_AuthoredGrid_RoundTrips()
        {
            const string grid = "...\nWWW\n###";
            string json = InitRequestSerializer.Serialize(BuildSampleRequest(authoredGridText: grid));
            var init = JsonSerializer.Deserialize<InitRequest>(json, Options)!;
            Assert.Equal(grid, init.AuthoredGridText);
        }
    }
}
