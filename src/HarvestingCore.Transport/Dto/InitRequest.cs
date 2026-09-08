using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HarvestingCore.Transport.Dto
{
    /// <summary>
    /// Client → Server authoring payload. The client is the sole author of the
    /// world: it sends this once, immediately after the WebSocket handshake, and
    /// the server builds its <c>SimulationWorld</c> from it before replying with a
    /// <c>state_response</c>.
    ///
    /// Mirrors the client's <c>SessionRequest</c> shape field-for-field.
    /// </summary>
    public sealed class InitRequest
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "init_request";

        [JsonPropertyName("width")]
        public int Width { get; set; }

        [JsonPropertyName("height")]
        public int Height { get; set; }

        [JsonPropertyName("seed")]
        public int Seed { get; set; }

        [JsonPropertyName("cropDensity")]
        public double CropDensity { get; set; }

        [JsonPropertyName("blockedDensity")]
        public double BlockedDensity { get; set; }

        /// <summary>Null when the world should be procedurally generated.</summary>
        [JsonPropertyName("authoredGridText")]
        public string? AuthoredGridText { get; set; }

        [JsonPropertyName("refuelStations")]
        public List<PositionDto> RefuelStations { get; set; } = new List<PositionDto>();

        [JsonPropertyName("dumpSites")]
        public List<PositionDto> DumpSites { get; set; } = new List<PositionDto>();

        [JsonPropertyName("agents")]
        public List<AgentSpecDto> Agents { get; set; } = new List<AgentSpecDto>();

        [JsonPropertyName("cropCost")]
        public int CropCost { get; set; }

        [JsonPropertyName("emptyCost")]
        public int EmptyCost { get; set; }

        [JsonPropertyName("harvestedCost")]
        public int HarvestedCost { get; set; }

        [JsonPropertyName("heuristicKind")]
        public int HeuristicKind { get; set; }

        [JsonPropertyName("defaultMaxLoad")]
        public int DefaultMaxLoad { get; set; }

        [JsonPropertyName("defaultMaxFuel")]
        public int DefaultMaxFuel { get; set; }

        [JsonPropertyName("defaultFuelConsumption")]
        public int DefaultFuelConsumption { get; set; }

        [JsonPropertyName("dumpPreferenceFactor")]
        public double DumpPreferenceFactor { get; set; }

        [JsonPropertyName("capacityFactor")]
        public double CapacityFactor { get; set; }

        [JsonPropertyName("harvesterFuelReserveMultiplier")]
        public double HarvesterFuelReserveMultiplier { get; set; }

        [JsonPropertyName("tractorFuelReserveMultiplier")]
        public double TractorFuelReserveMultiplier { get; set; }
    }

    /// <summary>A grid position on the wire.</summary>
    public sealed class PositionDto
    {
        [JsonPropertyName("x")]
        public int X { get; set; }

        [JsonPropertyName("y")]
        public int Y { get; set; }
    }

    /// <summary>One authored agent to register at world build time.</summary>
    public sealed class AgentSpecDto
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        /// <summary>"Harvester" or "Tractor".</summary>
        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;

        [JsonPropertyName("x")]
        public int X { get; set; }

        [JsonPropertyName("y")]
        public int Y { get; set; }

        [JsonPropertyName("maxLoad")]
        public int? MaxLoad { get; set; }

        [JsonPropertyName("maxFuel")]
        public int? MaxFuel { get; set; }

        [JsonPropertyName("fuelConsumption")]
        public int? FuelConsumption { get; set; }
    }
}
