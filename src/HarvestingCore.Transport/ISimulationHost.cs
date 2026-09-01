using System.Threading;
using System.Threading.Tasks;
using HarvestingCore.Transport.Dto;

namespace HarvestingCore.Transport
{
    /// <summary>
    /// Abstraction the transport layer uses to tick the simulation and read state.
    /// The host process implements this; HarvestingCore.Transport never references
    /// SimulationWorld or any other core type directly (Requirement 6.2).
    /// </summary>
    public interface ISimulationHost
    {
        /// <summary>Whether the simulation has reached a terminal state (Req 6.2).</summary>
        bool IsHalted { get; }

        /// <summary>Advances the simulation by one tick (Req 6.2).</summary>
        Task TickAsync(CancellationToken ct);

        /// <summary>Returns the current simulation state as a serialisable snapshot (Req 6.2).</summary>
        SimulationSnapshot GetSnapshot();
    }
}
