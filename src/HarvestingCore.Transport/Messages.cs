using System.Text.Json.Serialization;
using HarvestingCore.Transport.Dto;

namespace HarvestingCore.Transport
{
    // ── Inbound messages ────────────────────────────────────────────────────────

    /// <summary>
    /// Client → Server: request one or more simulation ticks.
    /// Schema: { "type": "tick_request", "count": &lt;integer&gt; }  (Req 5.3)
    /// </summary>
    internal sealed class TickRequest
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "tick_request";

        [JsonPropertyName("count")]
        public int Count { get; set; }
    }

    /// <summary>
    /// Client → Server: request the current snapshot without advancing the simulation.
    /// Schema: { "type": "state_request" }  (Req 5.5)
    /// </summary>
    internal sealed class StateRequest
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "state_request";
    }

    // ── Outbound messages ───────────────────────────────────────────────────────

    /// <summary>
    /// Server → Client: emitted after each individual tick.
    /// Schema: { "type": "tick_response", "tick": &lt;integer&gt;, "snapshot": &lt;SimulationSnapshot&gt; }  (Req 5.4)
    /// </summary>
    internal sealed class TickResponse
    {
        [JsonPropertyName("type")]
        public string Type => "tick_response";

        [JsonPropertyName("tick")]
        public int Tick { get; set; }

        [JsonPropertyName("snapshot")]
        public SimulationSnapshot Snapshot { get; set; } = null!;
    }

    /// <summary>
    /// Server → Client: reply to a StateRequest.
    /// Schema: { "type": "state_response", "tick": &lt;integer&gt;, "snapshot": &lt;SimulationSnapshot&gt; }  (Req 5.6)
    /// </summary>
    internal sealed class StateResponse
    {
        [JsonPropertyName("type")]
        public string Type => "state_response";

        [JsonPropertyName("tick")]
        public int Tick { get; set; }

        [JsonPropertyName("snapshot")]
        public SimulationSnapshot Snapshot { get; set; } = null!;
    }

    /// <summary>
    /// Server → Client: signals that a request could not be fulfilled.
    /// Schema: { "type": "error_response", "code": &lt;string&gt;, "message": &lt;string&gt; }  (Req 5.7)
    /// </summary>
    internal sealed class ErrorResponse
    {
        [JsonPropertyName("type")]
        public string Type => "error_response";

        [JsonPropertyName("code")]
        public string Code { get; set; } = null!;

        [JsonPropertyName("message")]
        public string Message { get; set; } = null!;
    }
}
