using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FsCheck;
using FsCheck.Xunit;
using HarvestingCore.Transport;
using HarvestingCore.Transport.Dto;
using Xunit;

namespace HarvestingCore.Transport.Tests
{
    /// <summary>
    /// Property-based tests for <see cref="TransportServer"/>.
    /// </summary>
    public class TransportServerPropertyTests
    {
        // ─── Fake ISimulationHost ──────────────────────────────────────────────

        /// <summary>
        /// A thread-safe stub that increments an atomic counter on each TickAsync call
        /// and records how many distinct clients received responses.
        /// </summary>
        private sealed class CountingHost : ISimulationHost
        {
            private int _tickCount;

            public bool IsHalted => false;

            public Task TickAsync(CancellationToken ct)
            {
                Interlocked.Increment(ref _tickCount);
                return Task.CompletedTask;
            }

            public SimulationSnapshot GetSnapshot() => new SimulationSnapshot
            {
                Tick = Volatile.Read(ref _tickCount),
                IsHalted = false,
                DischargedTotal = 0,
                Agents = new List<AgentSnapshot>(),
                Cells = new List<CellSnapshot>(),
            };
        }

        // ─── Port helper ──────────────────────────────────────────────────────

        /// <summary>
        /// Picks an available TCP port by briefly binding a listener, recording the
        /// OS-assigned ephemeral port, then releasing it before the caller opens theirs.
        /// This minimises (but cannot fully eliminate) TOCTOU races on a busy machine.
        /// </summary>
        private static int FindFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        // ─── Helper: send a state_request and return the parsed response type ─

        private static readonly byte[] StateRequestBytes =
            Encoding.UTF8.GetBytes(@"{""type"":""state_request""}");

        /// <summary>
        /// Connects a <see cref="ClientWebSocket"/> to <paramref name="port"/>,
        /// sends one <c>state_request</c>, reads one response, and returns the
        /// <c>type</c> field from the JSON response.
        /// Closes the connection cleanly afterwards.
        /// </summary>
        private static async Task<string?> ConnectSendReceiveAsync(int port, CancellationToken ct)
        {
            using var ws = new ClientWebSocket();
            var uri = new Uri($"ws://127.0.0.1:{port}/");
            await ws.ConnectAsync(uri, ct);

            // Send state_request
            await ws.SendAsync(
                new ArraySegment<byte>(StateRequestBytes),
                WebSocketMessageType.Text,
                endOfMessage: true,
                cancellationToken: ct);

            // Receive the response (accumulate frames)
            var buffer = new byte[4096];
            using var ms = new System.IO.MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (result.MessageType == WebSocketMessageType.Close)
                    break;
                ms.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            // Parse the type field
            string? responseType = null;
            if (ms.Length > 0)
            {
                var json = Encoding.UTF8.GetString(ms.ToArray());
                try
                {
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("type", out var typeProp))
                        responseType = typeProp.GetString();
                }
                catch (JsonException)
                {
                    // leave responseType null
                }
            }

            // Graceful close
            if (ws.State == WebSocketState.Open)
            {
                await ws.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Done",
                    CancellationToken.None);
            }

            return responseType;
        }

        // ─── Property 3: concurrent connections ──────────────────────────────

        /// <summary>
        /// Property 3: For any N ∈ [1, 10], a <see cref="TransportServer"/> accepts and
        /// independently handles N simultaneous WebSocket connections.
        /// Each client sends one <c>state_request</c> and must receive a <c>state_response</c>.
        /// Validates: Requirements 2.4
        /// </summary>
        [Property]
        public Property ConcurrentConnections_AllClientsReceiveStateResponse()
        {
            // Generate N in [1, 10]
            var genN = Gen.Choose(1, 10);

            return Prop.ForAll(genN.ToArbitrary(), n =>
            {
                // Run the async scenario synchronously for FsCheck
                return RunScenario(n).GetAwaiter().GetResult();
            });
        }

        private static async Task<Property> RunScenario(int n)
        {
            int port = FindFreePort();
            var host = new CountingHost();
            await using var server = new TransportServer(port, host);

            await server.StartAsync();

            // Give the HttpListener a moment to start accepting
            await Task.Delay(50);

            // Connect N clients concurrently, each sending one state_request
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var tasks = new Task<string?>[n];
            for (int i = 0; i < n; i++)
                tasks[i] = ConnectSendReceiveAsync(port, cts.Token);

            string?[] results;
            try
            {
                results = await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                return false.Label($"One or more client tasks threw an exception: {ex.Message}; N={n}");
            }

            // Every client must have received a "state_response"
            int successCount = 0;
            for (int i = 0; i < n; i++)
            {
                if (results[i] == "state_response")
                    successCount++;
            }

            await server.StopAsync();

            return (successCount == n)
                .Label($"Expected {n} state_response(s), got {successCount}; N={n}");
        }
    }
}
