using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace HarvestingCore.Transport
{
    /// <summary>
    /// WebSocket server that accepts client connections and routes JSON messages
    /// to the simulation through <see cref="ISimulationHost"/>.
    /// Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 2.1, 2.2, 2.3, 2.4, 2.5
    /// </summary>
    public sealed class TransportServer : IAsyncDisposable
    {
        private readonly int _port;
        private readonly ISimulationHost _host;

        // Active connections indexed by connection id (Req 2.4)
        private readonly ConcurrentDictionary<Guid, ClientHandler> _connections = new();

        // Track the RunAsync tasks so StopAsync can await them
        private readonly ConcurrentDictionary<Guid, Task> _connectionTasks = new();

        private HttpListener? _listener;
        private CancellationTokenSource? _cts;
        private Task? _acceptLoop;
        private MessageDispatcher? _dispatcher;

        // Guards against double-start and double-stop
        private int _state; // 0 = idle, 1 = running, 2 = stopped
        private const int StateIdle    = 0;
        private const int StateRunning = 1;
        private const int StateStopped = 2;

        /// <param name="port">TCP port to listen on.</param>
        /// <param name="host">Simulation host the dispatcher will call.</param>
        public TransportServer(int port, ISimulationHost host)
        {
            if (port is < 1 or > 65535)
                throw new ArgumentOutOfRangeException(nameof(port), "Port must be between 1 and 65535.");
            _port = port;
            _host = host ?? throw new ArgumentNullException(nameof(host));
        }

        // ── Start ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Starts the WebSocket server and begins accepting connections.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if already running (Req 1.4).</exception>
        /// <remarks>Socket exceptions from <see cref="HttpListener"/> propagate to the caller (Req 1.5).</remarks>
        public Task StartAsync(CancellationToken ct = default)
        {
            // Req 1.4: throw if already running
            if (Interlocked.CompareExchange(ref _state, StateRunning, StateIdle) != StateIdle)
                throw new InvalidOperationException("TransportServer is already running.");

            _dispatcher = new MessageDispatcher(_host);
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://+:{_port}/");

            // Let socket exceptions propagate to the caller (Req 1.5)
            _listener.Start();

            _acceptLoop = AcceptLoopAsync(_cts.Token);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Accept loop: upgrades HTTP requests to WebSocket connections and spawns
        /// a <see cref="ClientHandler"/> task for each one.
        /// While stopped it rejects new requests with 503 (Req 2.5).
        /// </summary>
        private async Task AcceptLoopAsync(CancellationToken serverCt)
        {
            while (!serverCt.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = await _listener!.GetContextAsync().WaitAsync(serverCt);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (HttpListenerException)
                {
                    // Listener was stopped (e.g. during StopAsync)
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                // Req 2.5: if cancellation was requested between the await and here,
                // reject with 503 and do not upgrade
                if (serverCt.IsCancellationRequested || !ctx.Request.IsWebSocketRequest)
                {
                    ctx.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                    ctx.Response.Close();
                    continue;
                }

                // Upgrade to WebSocket (Req 2.1)
                HttpListenerWebSocketContext wsCtx;
                try
                {
                    wsCtx = await ctx.AcceptWebSocketAsync(subProtocol: null);
                }
                catch (Exception)
                {
                    ctx.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                    ctx.Response.Close();
                    continue;
                }

                SpawnClientHandler(wsCtx.WebSocket, serverCt);
            }
        }

        /// <summary>
        /// Creates a <see cref="ClientHandler"/> for <paramref name="webSocket"/>,
        /// registers it in the active-connections map, and starts its task.
        /// </summary>
        private void SpawnClientHandler(WebSocket webSocket, CancellationToken serverCt)
        {
            var id = Guid.NewGuid();
            var handler = new ClientHandler(id, webSocket, _dispatcher!, RemoveConnection);
            _connections[id] = handler;

            var task = handler.RunAsync(serverCt);
            handler.Completion = task;
            _connectionTasks[id] = task;

            // Clean up the task entry when the handler finishes (fire-and-forget housekeeping)
            _ = task.ContinueWith(_ => _connectionTasks.TryRemove(id, out _),
                TaskContinuationOptions.ExecuteSynchronously);
        }

        private void RemoveConnection(Guid id)
        {
            _connections.TryRemove(id, out _);
        }

        // ── Stop ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Stops the server: cancels the accept loop, closes the listener, sends close
        /// code 1001 (Going Away) to all active connections, and awaits their completion.
        /// Safe to call multiple times; subsequent calls are no-ops.
        /// Requirements: 1.3, 2.2
        /// </summary>
        public async Task StopAsync()
        {
            // Only the first caller transitions to stopped; others return immediately
            if (Interlocked.Exchange(ref _state, StateStopped) != StateRunning)
                return;

            // 1. Cancel the accept loop and all handlers
            _cts?.Cancel();

            // 2. Stop the HttpListener so GetContextAsync unblocks
            try { _listener?.Stop(); } catch { /* best-effort */ }

            // 3. Await the accept loop
            if (_acceptLoop is not null)
            {
                try { await _acceptLoop; } catch { /* already cancelled */ }
            }

            // 4. Await all active ClientHandler tasks; the handlers send 1001 on OperationCanceledException
            var pending = _connectionTasks.Values.ToArray();
            if (pending.Length > 0)
            {
                try { await Task.WhenAll(pending); } catch { /* individual handler errors are swallowed */ }
            }

            // 5. Clean up
            _cts?.Dispose();
            _cts = null;
            _listener?.Close();
            _listener = null;
        }

        // ── IAsyncDisposable ────────────────────────────────────────────────────

        /// <summary>
        /// Calls <see cref="StopAsync"/> when the server has not already been stopped.
        /// Requirement 1.6
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (_state != StateStopped)
                await StopAsync();
        }
    }
}
