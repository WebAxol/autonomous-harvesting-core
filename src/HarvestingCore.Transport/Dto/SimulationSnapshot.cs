using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HarvestingCore.Transport.Dto
{
    /// <summary>
    /// Represents the full observable state of the simulation at a given tick.
    /// Requirement 5.8: tick (integer), isHalted (boolean), dischargedTotal (integer),
    /// agents (array of AgentSnapshot), cells (array of CellSnapshot).
    /// </summary>
    public sealed class SimulationSnapshot
    {
        [JsonPropertyName("tick")]
        public int Tick { get; set; }

        [JsonPropertyName("isHalted")]
        public bool IsHalted { get; set; }

        [JsonPropertyName("dischargedTotal")]
        public int DischargedTotal { get; set; }

        [JsonPropertyName("agents")]
        public List<AgentSnapshot> Agents { get; set; } = new List<AgentSnapshot>();

        [JsonPropertyName("cells")]
        public List<CellSnapshot> Cells { get; set; } = new List<CellSnapshot>();
    }
}
