using System.Text.Json.Serialization;

namespace HarvestingCore.Transport.Dto
{
    /// <summary>
    /// Represents the observable properties of a single agent at a given tick.
    /// Requirement 5.9: id (string), role (string), state (string),
    /// x (integer), y (integer), fuel (integer), load (integer).
    /// </summary>
    public sealed class AgentSnapshot
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        /// <summary>"Harvester" or "Tractor"</summary>
        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;

        /// <summary>StateId.ToString() value for the agent's current FSM state.</summary>
        [JsonPropertyName("state")]
        public string State { get; set; } = string.Empty;

        [JsonPropertyName("x")]
        public int X { get; set; }

        [JsonPropertyName("y")]
        public int Y { get; set; }

        [JsonPropertyName("fuel")]
        public int Fuel { get; set; }

        [JsonPropertyName("load")]
        public int Load { get; set; }

        [JsonPropertyName("maxLoad")]
        public int MaxLoad { get; set; }

        [JsonPropertyName("pathInvalidatedThisTick")]
        public bool PathInvalidatedThisTick { get; set; }

        [JsonPropertyName("meetingPointX")]
        public int? MeetingPointX { get; set; }

        [JsonPropertyName("meetingPointY")]
        public int? MeetingPointY { get; set; }
    }
}
