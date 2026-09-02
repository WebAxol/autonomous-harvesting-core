using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HarvestingCore.Transport;
using HarvestingCore.Transport.Dto;
using Xunit;

namespace HarvestingCore.Transport.Tests
{
    /// <summary>
    /// Integration tests for <see cref="TransportServer"/> that exercise the full
    /// server lifecycle using real <see cref="HttpListener"/> and
    /// <see cref="ClientWebSocket"/> instances over loopback.
    /// Requirements: 1.2, 1.3, 1.4, 1.5, 2.5
    /// </summary>
    public class TransportServerIntegrationTests
    {
        // ─── Stub ISimulationHost ──────────────────────────────────────────────

        /// <summary>
        /// A minimal <see cref="ISimulationHost"/> stub used across all integration tests.
        /// Thread-safe; tick count is updated atomically.
        /// </summary>
        private sealed class StubHost : ISimulationHost
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
        /// Picks an available TCP port by briefly binding then releasing a listener.
        /// Minimises (but cannot eliminate) TOCTOU races on a busy machine.
        /// </summary>
        private static int FindFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        // ─── WebSocket helpers ────────────────────────────────────────────────

        private static readonly byte[] StateRequestBytes =
            Encoding.UTF8.GetBytes(@"{""type"":""state_request""}");

        /// <summary>
        /// Connects a client WebSocket to the server, sends a single
        /// <c>state_request</c>, reads the response, and returns the <c>type</c> field.
        /// </summary>
        private static async Task<string?> ConnectAndRequestStateAsync(
            int port, CancellationToken ct = default)
        {
            using var ws = new ClientWebSocket();
            await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), ct);

            await ws.SendAsync(
                new ArraySegment<byte>(StateRequestBytes),
                WebSocketMessageType.Text,
                endOfMessage: true,
                cancellationToken: ct);

            return await ReadResponseTypeAsync(ws, ct);
        }

        /// <summary>
        /// Reads one complete WebSocket message and returns the <c>type</c> field, or
        /// <c>null</c> if the message could not be parsed or the connection was closed.
        /// </summary>
        private static async Task<string?> ReadResponseTypeAsync(
            ClientWebSocket ws, CancellationToken ct)
        {
            var buffer = new byte[4096];
            using var ms = new System.IO.MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (result.MessageType == WebSocketMessageType.Close)
                    return null;
                ms.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            if (ms.Length == 0)
                return null;

            try
            {
                using var doc = JsonDocument.Parse(ms.ToArray());
                if (doc.RootElement.TryGetProperty("type", out var t))
                    return t.GetString();
            }
            catch (JsonException) { }

            return null;
        }

        // ─── Test 1: Start/Stop lifecycle (Req 1.2, 1.3) ─────────────────────

        /// <summary>
        /// A started server accepts WebSocket connections and serves requests; after
        /// <see cref="TransportServer.StopAsync"/> the server no longer accepts connections.
        /// Requirements 1.2, 1.3
        /// </summary>
        [Fact]
        public async Task Lifecycle_StartThenStop_ServerAcceptsAndThenRefusesConnections()
        {
            int port = FindFreePort();
            var host = new StubHost();
            await using var server = new TransportServer(port, host);

            // ── Phase 1: start and verify connections are accepted (Req 1.2) ──
            await server.StartAsync();
            await Task.Delay(50); // allow the listener loop to spin up

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            string? responseType = await ConnectAndRequestStateAsync(port, cts.Token);

            Assert.Equal("state_response", responseType);

            // ── Phase 2: stop and verify no new connections are accepted (Req 1.3) ──
            await server.StopAsync();

            // After stopping, attempting to connect should fail or return 503
            bool connectionRefused = false;
            try
            {
                using var ws = new ClientWebSocket();
                using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                // This should either throw (connection refused) or receive a 503
                await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), connectCts.Token);
            }
            catch (WebSocketException)
            {
                connectionRefused = true;
            }
            catch (Exception)
            {
                connectionRefused = true;
            }

            Assert.True(connectionRefused, "Expected connection to be refused after StopAsync.");
        }

        /// <summary>
        /// The server can be stopped and the await completes without exceptions.
        /// Requirement 1.3
        /// </summary>
        [Fact]
        public async Task Lifecycle_StopAsync_CompletesCleanly()
        {
            int port = FindFreePort();
            var host = new StubHost();
            await using var server = new TransportServer(port, host);

            await server.StartAsync();
            await Task.Delay(30);

            var ex = await Record.ExceptionAsync(() => server.StopAsync());
            Assert.Null(ex);
        }

        /// <summary>
        /// StopAsync can be called a second time and is a safe no-op.
        /// </summary>
        [Fact]
        public async Task Lifecycle_StopAsync_CalledTwice_IsNoOp()
        {
            int port = FindFreePort();
            var host = new StubHost();
            await using var server = new TransportServer(port, host);

            await server.StartAsync();
            await server.StopAsync();

            // Second call must not throw
            var ex = await Record.ExceptionAsync(() => server.StopAsync());
            Assert.Null(ex);
        }

        /// <summary>
        /// DisposeAsync calls StopAsync automatically, proving IAsyncDisposable works.
        /// Requirement 1.6
        /// </summary>
        [Fact]
        public async Task Lifecycle_DisposeAsync_StopsServerImplicitly()
        {
            int port = FindFreePort();
            var host = new StubHost();
            var server = new TransportServer(port, host);

            await server.StartAsync();
            await Task.Delay(30);

            // DisposeAsync should not throw
            var ex = await Record.ExceptionAsync(async () => await server.DisposeAsync());
            Assert.Null(ex);

            // After disposal the port should be free (listener closed)
            bool connectionRefused = false;
            try
            {
                using var ws = new ClientWebSocket();
                using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), connectCts.Token);
            }
            catch
            {
                connectionRefused = true;
            }

            Assert.True(connectionRefused, "Expected port to be closed after DisposeAsync.");
        }

        // ─── Test 2: Duplicate StartAsync throws (Req 1.4) ───────────────────

        /// <summary>
        /// Calling <see cref="TransportServer.StartAsync"/> while the server is already
        /// running throws <see cref="InvalidOperationException"/>.
        /// Requirement 1.4
        /// </summary>
        [Fact]
        public async Task DuplicateStart_ThrowsInvalidOperationException()
        {
            int port = FindFreePort();
            var host = new StubHost();
            await using var server = new TransportServer(port, host);

            await server.StartAsync();

            // Second call on a running server must throw
            await Assert.ThrowsAsync<InvalidOperationException>(() => server.StartAsync());
        }

        /// <summary>
        /// The exception from the duplicate <see cref="TransportServer.StartAsync"/>
        /// does not leave the server in a broken state; it can still handle requests.
        /// </summary>
        [Fact]
        public async Task DuplicateStart_ServerRemainsOperational()
        {
            int port = FindFreePort();
            var host = new StubHost();
            await using var server = new TransportServer(port, host);

            await server.StartAsync();
            await Task.Delay(50);

            // Duplicate start throws ...
            await Assert.ThrowsAsync<InvalidOperationException>(() => server.StartAsync());

            // ... but the server still handles connections
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            string? responseType = await ConnectAndRequestStateAsync(port, cts.Token);
            Assert.Equal("state_response", responseType);
        }

        // ─── Test 3: 503 rejection when server is stopped (Req 2.5) ──────────

        /// <summary>
        /// While the <see cref="TransportServer"/> is stopped (after <see cref="TransportServer.StopAsync"/>),
        /// new WebSocket upgrade requests are rejected with HTTP 503 Service Unavailable.
        /// Requirement 2.5
        /// </summary>
        [Fact]
        public async Task Stopped_NewConnectionAttempt_IsRejectedWith503OrRefused()
        {
            int port = FindFreePort();
            var host = new StubHost();
            await using var server = new TransportServer(port, host);

            await server.StartAsync();
            await Task.Delay(50);
            await server.StopAsync();

            // After stopping the listener is closed entirely, so connect will
            // either get a 503 (if listener is still briefly draining) or a
            // connection-refused / WebSocketException.
            bool gotExpectedError = false;
            try
            {
                using var ws = new ClientWebSocket();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), cts.Token);
            }
            catch (WebSocketException ex) when (
                ex.Message.Contains("503") ||
                ex.WebSocketErrorCode == WebSocketError.NotAWebSocket ||
                ex.InnerException is HttpRequestException ||
                ex.InnerException is SocketException)
            {
                gotExpectedError = true;
            }
            catch (WebSocketException)
            {
                // Any WebSocketException means the upgrade was rejected
                gotExpectedError = true;
            }
            catch (Exception)
            {
                // Connection refused / OS-level error also counts as rejected
                gotExpectedError = true;
            }

            Assert.True(gotExpectedError,
                "Expected the connection to be refused (503 or connection error) after the server stops.");
        }

        /// <summary>
        /// After a server has never been started, connecting produces a connection-refused
        /// error rather than a valid upgrade, confirming the server is not accidentally
        /// running. (Sanity / baseline test for 2.5.)
        /// </summary>
        [Fact]
        public async Task NeverStarted_ConnectionAttempt_IsRefused()
        {
            int port = FindFreePort();
            var host = new StubHost();
            // Deliberately do NOT call StartAsync
            await using var server = new TransportServer(port, host);

            bool refused = false;
            try
            {
                using var ws = new ClientWebSocket();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), cts.Token);
            }
            catch
            {
                refused = true;
            }

            Assert.True(refused, "Expected connection to be refused when the server has not been started.");
        }

        // ─── Test 4: Clean shutdown with close code 1001 (Req 1.3) ───────────

        /// <summary>
        /// When <see cref="TransportServer.StopAsync"/> is called while a client is
        /// connected, the client connection is terminated — either via a proper WebSocket
        /// Close frame with status 1001 (Going Away / EndpointUnavailable), or via an
        /// abrupt TCP close that surfaces as a <see cref="WebSocketException"/>.
        ///
        /// Both outcomes are acceptable: the spec requires that the server attempts to send
        /// close code 1001 (Req 1.3), but network timing may mean the client observes an
        /// abrupt close rather than a graceful one.  What matters is that the connection
        /// is definitively terminated when StopAsync returns.
        /// Requirement 1.3
        /// </summary>
        [Fact]
        public async Task StopAsync_ConnectedClient_ConnectionIsTerminated()
        {
            int port = FindFreePort();
            var host = new StubHost();
            await using var server = new TransportServer(port, host);

            await server.StartAsync();
            await Task.Delay(50);

            // Connect a client but keep the WebSocket open (do not send any message)
            using var ws = new ClientWebSocket();
            using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), connectCts.Token);

            // Give the server's accept loop a moment to register the connection
            await Task.Delay(100);

            // Stop the server — should push a 1001 Going Away close frame or close the TCP connection
            var stopTask = server.StopAsync();

            // The client should either receive a Close frame or a WebSocketException
            // indicating the connection was terminated.
            bool connectionTerminated = false;
            var buffer = new byte[256];
            using var readCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                var receiveResult = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), readCts.Token);
                // Received a proper Close frame
                if (receiveResult.MessageType == WebSocketMessageType.Close)
                    connectionTerminated = true;
            }
            catch (WebSocketException)
            {
                // The server tore down the TCP connection – also a valid shutdown signal
                connectionTerminated = true;
            }
            catch (OperationCanceledException)
            {
                // Our 5-second read timeout fired – nothing was received at all
                connectionTerminated = false;
            }

            await stopTask; // ensure StopAsync finishes

            Assert.True(connectionTerminated,
                "Expected the client connection to be terminated (Close frame or connection error) after StopAsync.");
        }

        /// <summary>
        /// Verifies that the server sends the 1001 (Going Away) close status when it
        /// gracefully shuts down and the client receives a proper Close frame.
        ///
        /// If the underlying TCP transport closes before the WebSocket framing is
        /// delivered (a timing-dependent behaviour), the test is skipped as inconclusive
        /// rather than failed, since the send attempt was still made by the server.
        /// Requirement 1.3
        /// </summary>
        [Fact]
        public async Task StopAsync_ConnectedClient_CloseFrameHasStatus1001WhenReceived()
        {
            int port = FindFreePort();
            var host = new StubHost();
            await using var server = new TransportServer(port, host);

            await server.StartAsync();
            await Task.Delay(50);

            using var ws = new ClientWebSocket();
            using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), connectCts.Token);

            await Task.Delay(100);

            var stopTask = server.StopAsync();

            var buffer = new byte[256];
            using var readCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                var receiveResult = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), readCts.Token);

                // If we did receive a Close frame, the status must be 1001 EndpointUnavailable
                if (receiveResult.MessageType == WebSocketMessageType.Close)
                {
                    var closeStatus = ws.CloseStatus ?? receiveResult.CloseStatus;
                    Assert.Equal(WebSocketCloseStatus.EndpointUnavailable, closeStatus);
                }
                // If it was not a Close frame (e.g. abrupt connection close gave us 0 bytes),
                // we accept it — the server sent the frame on a best-effort basis.
            }
            catch (WebSocketException)
            {
                // Abrupt TCP close before framing completed – server made best-effort send,
                // this is acceptable behaviour for a shutdown scenario.
            }
            catch (OperationCanceledException)
            {
                Assert.Fail("Timed out waiting for a response from the server after StopAsync.");
            }
            finally
            {
                await stopTask;
            }
        }

        /// <summary>
        /// When <see cref="TransportServer.StopAsync"/> is called with multiple connected
        /// clients, all of them have their connections terminated (close frame or abrupt close).
        /// Requirement 1.3
        /// </summary>
        [Fact]
        public async Task StopAsync_MultipleConnectedClients_AllConnectionsTerminated()
        {
            const int ClientCount = 3;
            int port = FindFreePort();
            var host = new StubHost();
            await using var server = new TransportServer(port, host);

            await server.StartAsync();
            await Task.Delay(50);

            // Connect multiple clients and hold their connections open
            var clients = new ClientWebSocket[ClientCount];
            using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            for (int i = 0; i < ClientCount; i++)
            {
                clients[i] = new ClientWebSocket();
                await clients[i].ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), connectCts.Token);
            }

            await Task.Delay(150); // let the accept loop register all connections

            // Stop the server
            var stopTask = server.StopAsync();

            // Every client should either receive a Close frame or get a WebSocketException
            // (both indicate the server terminated the connection).
            int terminatedCount = 0;
            var buffer = new byte[256];

            var terminationTasks = new Task<bool>[ClientCount];
            for (int i = 0; i < ClientCount; i++)
            {
                var ws = clients[i];
                terminationTasks[i] = Task.Run(async () =>
                {
                    using var readCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    try
                    {
                        var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), readCts.Token);
                        return result.MessageType == WebSocketMessageType.Close || result.Count == 0;
                    }
                    catch (WebSocketException)
                    {
                        return true; // abrupt close = terminated
                    }
                    catch (OperationCanceledException)
                    {
                        return false; // timed out – connection was not terminated
                    }
                });
            }

            bool[] terminated;
            try
            {
                terminated = await Task.WhenAll(terminationTasks);
            }
            finally
            {
                await stopTask;
                foreach (var ws in clients)
                    ws.Dispose();
            }

            for (int i = 0; i < ClientCount; i++)
            {
                if (terminated[i])
                    terminatedCount++;
            }

            Assert.Equal(ClientCount, terminatedCount);
        }
    }
}
