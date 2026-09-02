using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HarvestingCore.Agents;
using HarvestingCore.Transport;
using HarvestingCore.Transport.Dto;
using HarvestingCore.World;

namespace HarvestingCore.Host
{
    /// <summary>
    /// Bridges SimulationWorld (the core library) to ISimulationHost (the transport layer).
    /// </summary>
    internal sealed class SimulationHostAdapter : ISimulationHost
    {
        private readonly SimulationWorld _world;

        public SimulationHostAdapter(SimulationWorld world) => _world = world;

        public bool IsHalted => _world.IsHalted;

        public Task TickAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _world.Tick();
            return Task.CompletedTask;
        }

        public SimulationSnapshot GetSnapshot()
        {
            var snapshot = new SimulationSnapshot
            {
                Tick = _world.TickIndex,
                IsHalted = _world.IsHalted,
                DischargedTotal = _world.DischargedTotal,
            };

            foreach (Agent agent in _world.Agents)
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
                });
            }

            int width = _world.Model.Width;
            IReadOnlyList<Cell> cells = _world.Model.Cells;
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
    }
}
