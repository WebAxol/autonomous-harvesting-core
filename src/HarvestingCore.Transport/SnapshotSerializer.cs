using System.Text;
using System.Text.Json;
using HarvestingCore.Transport.Dto;

namespace HarvestingCore.Transport
{
    /// <summary>
    /// Provides JSON serialisation and deserialisation for simulation DTOs.
    /// Requirements: 5.1, 7.1, 7.2, 7.4
    /// </summary>
    internal static class SnapshotSerializer
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
        };

        /// <summary>
        /// Serializes <paramref name="message"/> to a UTF-8 JSON byte array.
        /// </summary>
        public static byte[] Serialize(object message)
        {
            return JsonSerializer.SerializeToUtf8Bytes(message, message.GetType(), Options);
        }

        /// <summary>
        /// Deserialises <paramref name="json"/> into a <see cref="SimulationSnapshot"/>.
        /// Returns <c>null</c> and a descriptive message in <paramref name="error"/> when the JSON is malformed.
        /// Never throws an unhandled exception (Req 7.4).
        /// </summary>
        public static SimulationSnapshot? Deserialize(string json, out string? error)
        {
            try
            {
                var snapshot = JsonSerializer.Deserialize<SimulationSnapshot>(json, Options);
                if (snapshot is null)
                {
                    error = "Deserialization produced a null result.";
                    return null;
                }

                error = null;
                return snapshot;
            }
            catch (JsonException ex)
            {
                error = $"Malformed JSON: {ex.Message}";
                return null;
            }
        }
    }
}
