using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HarvestingCore.Agents;
using HarvestingCore.Configuration;
using HarvestingCore.Transport;
using HarvestingCore.Transport.Dto;
using HarvestingCore.World;

namespace HarvestingCore.Host
{
    /// <summary>
    /// Bridges SimulationWorld (the core library) to ISimulationHost (the transport layer).
    ///
    /// The client is the sole author of the world: this adapter starts with no world
    /// and builds one on the first <c>init_request</c> (see <see cref="Initialize"/>).
    /// Ticking or snapshotting before initialisation is rejected by the dispatcher.
    /// </summary>
    internal sealed class SimulationHostAdapter : ISimulationHost
    {
        private readonly object _gate = new object();
        private SimulationWorld? _world;

        public bool IsInitialized
        {
            get { lock (_gate) { return _world != null; } }
        }

        public bool IsHalted
        {
            get { lock (_gate) { return _world != null && _world.IsHalted; } }
        }

        public bool Initialize(InitRequest request, out string? error)
        {
            SimulationWorld? world = TryBuildWorld(request, out error);
            if (world == null)
                return false;

            lock (_gate)
            {
                _world = world;
            }
            return true;
        }

        public Task TickAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            SimulationWorld world = RequireWorld();
            world.Tick();
            return Task.CompletedTask;
        }

        public SimulationSnapshot GetSnapshot()
        {
            SimulationWorld world = RequireWorld();

            var snapshot = new SimulationSnapshot
            {
                Tick = world.TickIndex,
                Width = world.Model.Width,
                Height = world.Model.Height,
                IsHalted = world.IsHalted,
                DischargedTotal = world.DischargedTotal,
            };

            foreach (Agent agent in world.Agents)
            {
                snapshot.Agents.Add(new AgentSnapshot
                {
                    Id = agent.Id,
                    Role = agent.Role.ToString(),
                    State = agent.CurrentState.ToString(),
                    X = agent.Position.X,
                    Y = agent.Position.Y,
                    Fuel = agent.Fuel,
                    Load = agent.Load,
                    MaxLoad = agent.MaxLoad,
                    PathInvalidatedThisTick = agent.PathInvalidatedThisTick,
                    MeetingPointX = agent.MeetingPoint?.X,
                    MeetingPointY = agent.MeetingPoint?.Y,
                });
            }

            int width = world.Model.Width;
            IReadOnlyList<Cell> cells = world.Model.Cells;
            for (int i = 0; i < cells.Count; i++)
            {
                Cell cell = cells[i];
                int x = i % width;
                int y = i / width;
                snapshot.Cells.Add(new CellSnapshot
                {
                    X = x,
                    Y = y,
                    State = cell.State.ToString(),
                    OwnerId = cell.OwnerId,
                });
            }

            return snapshot;
        }

        private SimulationWorld RequireWorld()
        {
            lock (_gate)
            {
                if (_world == null)
                    throw new InvalidOperationException("Simulation has not been initialised. Send an init_request first.");
                return _world;
            }
        }

        // ── World authoring (mirrors the client's InMemory TryBuildWorld recipe) ──

        private static SimulationWorld? TryBuildWorld(InitRequest request, out string? error)
        {
            error = null;

            if (request == null)
            {
                error = "init_request payload was null.";
                return null;
            }

            SimulationConfig config;
            try
            {
                config = new SimulationConfig(
                    dumpPreferenceFactor: request.DumpPreferenceFactor,
                    capacityFactor: request.CapacityFactor,
                    harvesterFuelReserveMultiplier: request.HarvesterFuelReserveMultiplier,
                    tractorFuelReserveMultiplier: request.TractorFuelReserveMultiplier,
                    cropCost: request.CropCost,
                    emptyCost: request.EmptyCost,
                    harvestedCost: request.HarvestedCost,
                    heuristic: (HeuristicKind)request.HeuristicKind,
                    defaultMaxLoad: request.DefaultMaxLoad,
                    defaultMaxFuel: request.DefaultMaxFuel,
                    defaultFuelConsumption: request.DefaultFuelConsumption,
                    seed: request.Seed,
                    cropDensity: request.CropDensity,
                    blockedDensity: request.BlockedDensity);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                error = "Invalid configuration: " + ex.Message;
                return null;
            }

            IRandomSource random = new DeterministicRandom(request.Seed);

            List<GridPosition> refuel = MapPositions(request.RefuelStations);
            List<GridPosition> dumps = MapPositions(request.DumpSites);

            bool isAuthored = request.AuthoredGridText != null;
            WorldModel model;
            try
            {
                if (isAuthored)
                {
                    model = WorldModel.Parse(request.AuthoredGridText!, refuel, dumps);
                }
                else
                {
                    model = new WorldModel(request.Width, request.Height, refuel, dumps);
                }
            }
            catch (ArgumentException ex)
            {
                error = (isAuthored ? "Authored grid: " : "World model: ") + ex.Message;
                return null;
            }

            var world = new SimulationWorld(model, config, random);

            if (!isAuthored)
            {
                world.GenerateGrid();
            }

            // Reject duplicate ids before constructing anything.
            var seenIds = new HashSet<string>();
            foreach (AgentSpecDto spec in request.Agents)
            {
                if (!seenIds.Add(spec.Id))
                {
                    error = "Duplicate agent identifier '" + spec.Id + "'.";
                    return null;
                }
            }

            // Validate each agent's start cell against the now-known grid.
            foreach (AgentSpecDto spec in request.Agents)
            {
                var pos = new GridPosition(spec.X, spec.Y);
                if (!model.InBounds(pos))
                {
                    error = "Agent '" + spec.Id + "' start position " + pos + " is out of bounds.";
                    return null;
                }
                if (model.CellAt(pos).State == CellState.Blocked)
                {
                    error = "Agent '" + spec.Id + "' start position " + pos + " is Blocked.";
                    return null;
                }
            }

            // Construct and register in sorted id order for deterministic ordering.
            var sortedSpecs = new List<AgentSpecDto>(request.Agents);
            sortedSpecs.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));

            foreach (AgentSpecDto spec in sortedSpecs)
            {
                var pos = new GridPosition(spec.X, spec.Y);
                Agent agent;
                try
                {
                    bool isHarvester = string.Equals(spec.Role, "Harvester", StringComparison.OrdinalIgnoreCase);
                    agent = isHarvester
                        ? new Harvester(spec.Id, pos, model, config, spec.MaxLoad, spec.MaxFuel, spec.FuelConsumption)
                        : (Agent)new Tractor(spec.Id, pos, model, config, spec.MaxLoad, spec.MaxFuel, spec.FuelConsumption);
                }
                catch (ArgumentException ex)
                {
                    error = "Core rejected agent '" + spec.Id + "': " + ex.Message;
                    return null;
                }

                world.Register(agent);
            }

            world.RedistributeAreas();

            return world;
        }

        private static List<GridPosition> MapPositions(List<PositionDto> positions)
        {
            var mapped = new List<GridPosition>(positions.Count);
            for (int i = 0; i < positions.Count; i++)
            {
                mapped.Add(new GridPosition(positions[i].X, positions[i].Y));
            }
            return mapped;
        }
    }
}
