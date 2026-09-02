using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HarvestingCore.Transport
{
    /// <summary>
    /// Manages a single WebSocket client connection: reads frames, dispatches messages,
    /// and writes responses back.
    /// Requirements: 2.1, 2.2, 2.3
    /// </summary>
    internal sealed class ClientHandler
    {
        private const int BufferSize = 8 * 1024; // 8 KB receive buffer

        private readonly WebSocket _webSocket;
        private readonly MessageDispatcher _dispatcher;
        private readonly Action<Guid> _removeCallback;

        /// <summary>Unique identifier for this connection.</summary>
        public Guid Id { get; }

        /// <summary>
        /// Task that completes when <see cref="RunAsync"/> finishes (set externally by
        /// <c>TransportServer</c> so it can await all handler tasks on shutdown).
        /// </summary>
        public Task? Completion { get; set; }

        public ClientHandler(Guid id, WebSocket webSocket, MessageDispatcher dispatcher, Action<Guid> removeCallback)
        {
            Id = id;
            _webSocket = webSocket ?? throw new ArgumentNullException(nameof(webSocket));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _removeCallback = removeCallback ?? throw new ArgumentNullException(nameof(removeCallback));
        }

        /// <summary>
        /// Reads WebSocket frames in a loop until the connection closes or
        /// <paramref name="serverCt"/> is cancelled.
        ///
        /// Frame accumulation: 8 KB segments are joined into a <see cref="MemoryStream"/>
        /// until <see cref="WebSocketReceiveResult.EndOfMessage"/> is true.
        /// Only Text frames are dispatched; Binary frames receive an error response (Req 5.2).
        /// On <see cref="WebSocketState.CloseReceived"/> the closing handshake is completed
        /// before the loop exits.
        /// Any unexpected <see cref="WebSocketException"/> is swallowed so that other
        /// connections are not affected (Req 2.3).
        /// The removal callback is always invoked in a <c>finally</c> block (Req 2.2).
        /// </summary>
        public async Task RunAsync(CancellationToken serverCt)
        {
            try
            {
                byte[] rentedBuffer = new byte[BufferSize];

                while (_webSocket.State == WebSocketState.Open)
                {
                    // Accumulate one complete message (potentially many frames)
                    using var messageStream = new MemoryStream();
                    WebSocketReceiveResult result;

                    do
                    {
                        // Respect cancellation between frames
                        serverCt.ThrowIfCancellationRequested();

                        try
                        {
                            result = await _webSocket.ReceiveAsync(
                                new ArraySegment<byte>(rentedBuffer),
                                serverCt);
                        }
                        catch (WebSocketException)
                        {
                            // Unexpected disconnect while reading – exit the outer loop
                            return;
                        }
                        catch (OperationCanceledException)
                        {
                            // Server shutting down
                            throw;
                        }

                        // Req 2.2: client initiated close
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await PerformClosingHandshakeAsync(serverCt);
                            return;
                        }

                        messageStream.Write(rentedBuffer, 0, result.Count);

                    } while (!result.EndOfMessage);

                    // We now have a complete message; dispatch it
                    byte[] payload = messageStream.ToArray();
                    byte[] response;

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        // Provide a send callback for intermediate responses (multi-tick streaming)
                        async Task SendIntermediateAsync(byte[] bytes)
                        {
                            await SendTextFrameAsync(bytes, serverCt);
                        }

                        response = await _dispatcher.HandleAsync(
                            new ReadOnlyMemory<byte>(payload),
                            SendIntermediateAsync,
                            serverCt);
                    }
                    else
                    {
                        // Binary frames: return an error, do not modify simulation state (Req 5.2)
                        response = BuildErrorBytes("unknown_type", "Binary WebSocket frames are not supported; send UTF-8 JSON text.");
                    }

                    // Check state again before writing – the socket may have closed during dispatch
                    if (_webSocket.State == WebSocketState.Open)
                    {
                        await SendTextFrameAsync(response, serverCt);
                    }
                }

                // Socket transitioned to CloseReceived outside the receive loop
                if (_webSocket.State == WebSocketState.CloseReceived)
                {
                    await PerformClosingHandshakeAsync(serverCt);
                }
            }
            catch (OperationCanceledException)
            {
                // Server is stopping; attempt a graceful close with 1001 Going Away
                await TryCloseGoingAwayAsync();
            }
            catch (WebSocketException)
            {
                // Unexpected disconnect – log-worthy but swallowed so other connections
                // are not affected (Req 2.3)
            }
            finally
            {
                // Always remove from the server's connection map and free the socket (Req 2.2)
                _removeCallback(Id);
                _webSocket.Dispose();
            }
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        /// <summary>
        /// Sends <paramref name="bytes"/> as a single UTF-8 text WebSocket frame.
        /// </summary>
        private Task SendTextFrameAsync(byte[] bytes, CancellationToken ct)
        {
            return _webSocket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                endOfMessage: true,
                cancellationToken: ct);
        }

        /// <summary>
        /// Completes the WebSocket closing handshake when the remote peer has sent a Close frame
        /// (<see cref="WebSocketState.CloseReceived"/>).
        /// </summary>
        private async Task PerformClosingHandshakeAsync(CancellationToken ct)
        {
            try
            {
                // Echo back an appropriate close status
                await _webSocket.CloseOutputAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Closing",
                    ct);
            }
            catch (WebSocketException)
            {
                // Socket may already be gone; ignore
            }
            catch (OperationCanceledException)
            {
                // Server shutting down during handshake; ignore
            }
        }

        /// <summary>
        /// Attempts to send a Going Away (1001) close frame when the server is stopping.
        /// Failures are silently swallowed – the remote may already be disconnected.
        /// </summary>
        private async Task TryCloseGoingAwayAsync()
        {
            try
            {
                if (_webSocket.State == WebSocketState.Open)
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await _webSocket.CloseOutputAsync(
                        WebSocketCloseStatus.EndpointUnavailable,
                        "Server stopping",
                        cts.Token);
                }
            }
            catch
            {
                // Best-effort only
            }
        }

        /// <summary>
        /// Builds a serialised <c>error_response</c> byte array without depending on
        /// <see cref="MessageDispatcher"/> (used for protocol-level errors such as binary frames).
        /// </summary>
        private static byte[] BuildErrorBytes(string code, string message)
        {
            return SnapshotSerializer.Serialize(new ErrorResponse
            {
                Code = code,
                Message = message,
            });
        }
    }
}
