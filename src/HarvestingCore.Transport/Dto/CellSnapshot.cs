using System.Text.Json.Serialization;

namespace HarvestingCore.Transport.Dto
{
    /// <summary>
    /// Represents the observable properties of a single grid cell at a given tick.
    /// Requirement 5.10: x (integer), y (integer), state (string), ownerId (string).
    /// </summary>
    public sealed class CellSnapshot
    {
        [JsonPropertyName("x")]
        public int X { get; set; }

        [JsonPropertyName("y")]
        public int Y { get; set; }

        /// <summary>CellState.ToString() value.</summary>
        [JsonPropertyName("state")]
        public string State { get; set; } = string.Empty;

        [JsonPropertyName("ownerId")]
        public string? OwnerId { get; set; }
    }
}
