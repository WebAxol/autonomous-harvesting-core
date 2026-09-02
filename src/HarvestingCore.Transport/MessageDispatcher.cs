using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HarvestingCore.Transport.Dto;

namespace HarvestingCore.Transport
{
    /// <summary>
    /// Routes inbound JSON messages to the appropriate handler and returns a serialised response.
    /// Requirements: 3.1–3.6, 4.1, 4.3, 5.2, 8.1, 8.2, 8.3, 8.4
    /// </summary>
    internal sealed class MessageDispatcher
    {
        private readonly ISimulationHost _host;
        private readonly SemaphoreSlim _tickLock = new SemaphoreSlim(1, 1);

        public MessageDispatcher(ISimulationHost host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
        }

        /// <summary>
        /// Deserialises the <paramref name="payload"/>, routes to the correct handler,
        /// and returns the serialised response bytes.
        ///
        /// For multi-tick requests, intermediate TickResponse messages are sent via
        /// <paramref name="sendAsync"/>; the final response is returned as the result.
        /// </summary>
        public async Task<byte[]> HandleAsync(
            ReadOnlyMemory<byte> payload,
            Func<byte[], Task> sendAsync,
            CancellationToken ct)
        {
            string? type = PeekType(payload);

            return type switch
            {
                "tick_request"  => await HandleTickRequestAsync(payload, sendAsync, ct),
                "state_request" => await HandleStateRequestAsync(ct),
                _               => SerializeError("unknown_type", "The 'type' field is absent or unrecognised.")
            };
        }

        // ── Type discriminator ──────────────────────────────────────────────────

        /// <summary>
        /// Peeks at the <c>type</c> field in the JSON payload without full deserialisation.
        /// Returns <c>null</c> if the field is absent or the payload is not valid JSON.
        /// </summary>
        private static string? PeekType(ReadOnlyMemory<byte> payload)
        {
            try
            {
                var reader = new Utf8JsonReader(payload.Span);
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.PropertyName &&
                        reader.GetString() == "type")
                    {
                        reader.Read();
                        return reader.GetString();
                    }
                }
                return null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        // ── Tick request handler ────────────────────────────────────────────────

        /// <summary>
        /// Validates, acquires the tick lock, runs the tick loop, and returns the
        /// final TickResponse bytes.  Intermediate responses are sent via <paramref name="sendAsync"/>.
        /// Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 8.1, 8.2, 8.4
        /// </summary>
        private async Task<byte[]> HandleTickRequestAsync(
            ReadOnlyMemory<byte> payload,
            Func<byte[], Task> sendAsync,
            CancellationToken ct)
        {
            TickRequest request;
            try
            {
                request = JsonSerializer.Deserialize<TickRequest>(payload.Span)
                    ?? throw new JsonException("Null result after deserialization.");
            }
            catch (JsonException ex)
            {
                return SerializeError("unknown_type", $"Could not parse tick_request: {ex.Message}");
            }

            // Req 3.3: count must be >= 1
            if (request.Count < 1)
            {
                return SerializeError("invalid_count", $"'count' must be >= 1, got {request.Count}.");
            }

            // Req 3.4: refuse if simulation is already halted
            if (_host.IsHalted)
            {
                return SerializeError("simulation_halted", "The simulation has halted and cannot be ticked.");
            }

            // Req 8.1, 8.4: serialise all TickAsync calls with a semaphore
            await _tickLock.WaitAsync(ct);
            try
            {
                byte[]? lastResponse = null;

                for (int i = 0; i < request.Count; i++)
                {
                    // Req 3.5: catch exceptions from TickAsync
                    try
                    {
                        await _host.TickAsync(ct);
                    }
                    catch (OperationCanceledException)
                    {
                        throw; // let cancellation propagate naturally
                    }
                    catch (Exception ex)
                    {
                        return SerializeError("tick_error", $"TickAsync failed: {ex.Message}");
                    }

                    SimulationSnapshot snapshot = _host.GetSnapshot();
                    var tickResponse = new TickResponse
                    {
                        Tick = snapshot.Tick,
                        Snapshot = snapshot,
                    };

                    byte[] responseBytes = SnapshotSerializer.Serialize(tickResponse);

                    bool isLast = (i == request.Count - 1);
                    if (!isLast)
                    {
                        // Send intermediate responses via the callback (Req 3.2)
                        await sendAsync(responseBytes);
                    }
                    else
                    {
                        lastResponse = responseBytes;
                    }
                }

                return lastResponse!;
            }
            finally
            {
                _tickLock.Release();
            }
        }

        // ── State request handler ───────────────────────────────────────────────

        /// <summary>
        /// Returns the current snapshot without acquiring the tick lock.
        /// Requirements: 4.1, 4.3, 8.3
        /// </summary>
        private Task<byte[]> HandleStateRequestAsync(CancellationToken ct)
        {
            try
            {
                SimulationSnapshot snapshot = _host.GetSnapshot();
                var stateResponse = new StateResponse
                {
                    Tick = snapshot.Tick,
                    Snapshot = snapshot,
                };
                return Task.FromResult(SnapshotSerializer.Serialize(stateResponse));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Task.FromResult(SerializeError("snapshot_error", $"GetSnapshot failed: {ex.Message}"));
            }
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        private static byte[] SerializeError(string code, string message)
        {
            return SnapshotSerializer.Serialize(new ErrorResponse
            {
                Code = code,
                Message = message,
            });
        }
    }
}
