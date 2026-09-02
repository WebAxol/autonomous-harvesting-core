using System;
using System.Collections.Generic;
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
    /// Unit tests for <see cref="ClientHandler"/>.
    /// Requirements: 2.2, 2.3
    /// </summary>
    public class ClientHandlerTests
    {
        // ─── Fake WebSocket ────────────────────────────────────────────────────

        /// <summary>
        /// A hand-rolled fake that replays a pre-configured sequence of receive results
        /// and captures everything that was sent back.
        /// </summary>
        private sealed class FakeWebSocket : WebSocket
        {
            private readonly Queue<Func<ArraySegment<byte>, CancellationToken, Task<WebSocketReceiveResult>>> _receiveHandlers;

            /// <summary>All byte arrays sent via SendAsync, in order.</summary>
            public List<byte[]> SentMessages { get; } = new List<byte[]>();

            /// <summary>How many times CloseOutputAsync was called.</summary>
            public int CloseOutputCallCount { get; private set; }

            /// <summary>The close status passed to the most recent CloseOutputAsync call.</summary>
            public WebSocketCloseStatus? LastCloseStatus { get; private set; }

            private WebSocketState _state = WebSocketState.Open;

            public FakeWebSocket(IEnumerable<Func<ArraySegment<byte>, CancellationToken, Task<WebSocketReceiveResult>>> handlers)
            {
                _receiveHandlers = new Queue<Func<ArraySegment<byte>, CancellationToken, Task<WebSocketReceiveResult>>>(handlers);
            }

            // ── WebSocket abstract member implementations ──────────────────────

            public override WebSocketCloseStatus? CloseStatus => null;
            public override string? CloseStatusDescription => null;
            public override WebSocketState State => _state;
            public override string? SubProtocol => null;

            public override void Abort() { _state = WebSocketState.Aborted; }
            public override void Dispose() { _state = WebSocketState.Closed; }

            public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
            {
                _state = WebSocketState.Closed;
                return Task.CompletedTask;
            }

            public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
            {
                CloseOutputCallCount++;
                LastCloseStatus = closeStatus;
                _state = WebSocketState.CloseSent;
                return Task.CompletedTask;
            }

            public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
            {
                if (_receiveHandlers.Count == 0)
                {
                    // No more receive handlers – simulate a clean close from the server perspective
                    _state = WebSocketState.Closed;
                    return Task.FromResult(new WebSocketReceiveResult(
                        0, WebSocketMessageType.Close, true,
                        WebSocketCloseStatus.NormalClosure, "End of fake sequence"));
                }

                var handler = _receiveHandlers.Dequeue();
                return handler(buffer, cancellationToken);
            }

            public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
            {
                // Capture a copy of the bytes
                var copy = new byte[buffer.Count];
                Buffer.BlockCopy(buffer.Array!, buffer.Offset, copy, 0, buffer.Count);
                SentMessages.Add(copy);
                return Task.CompletedTask;
            }
        }

        // ─── Fake ISimulationHost ──────────────────────────────────────────────

        private sealed class StubHost : ISimulationHost
        {
            public bool IsHalted { get; set; } = false;
            public int TickCount { get; private set; }

            public Task TickAsync(CancellationToken ct)
            {
                TickCount++;
                return Task.CompletedTask;
            }

            public SimulationSnapshot GetSnapshot() => new SimulationSnapshot
            {
                Tick = TickCount,
                IsHalted = IsHalted,
                DischargedTotal = 0,
                Agents = new List<AgentSnapshot>(),
                Cells = new List<CellSnapshot>(),
            };
        }

        // ─── Helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// Creates a receive handler that delivers a complete text message in a single frame.
        /// </summary>
        private static Func<ArraySegment<byte>, CancellationToken, Task<WebSocketReceiveResult>>
            TextFrame(string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            return (buffer, ct) =>
            {
                // Copy bytes into the provided buffer
                int count = Math.Min(bytes.Length, buffer.Count);
                Buffer.BlockCopy(bytes, 0, buffer.Array!, buffer.Offset, count);
                return Task.FromResult(new WebSocketReceiveResult(count, WebSocketMessageType.Text, endOfMessage: true));
            };
        }

        /// <summary>
        /// Creates a receive handler that delivers a WebSocket Close frame.
        /// </summary>
        private static Func<ArraySegment<byte>, CancellationToken, Task<WebSocketReceiveResult>>
            CloseFrame() =>
            (buffer, ct) => Task.FromResult(new WebSocketReceiveResult(
                0, WebSocketMessageType.Close, endOfMessage: true,
                WebSocketCloseStatus.NormalClosure, "Client closing"));

        /// <summary>
        /// Creates a receive handler that throws a <see cref="WebSocketException"/>,
        /// simulating an unexpected disconnect.
        /// </summary>
        private static Func<ArraySegment<byte>, CancellationToken, Task<WebSocketReceiveResult>>
            ThrowWebSocketException() =>
            (buffer, ct) => throw new WebSocketException(WebSocketError.ConnectionClosedPrematurely);

        /// <summary>
        /// Builds a <see cref="ClientHandler"/> wired to the given socket and host,
        /// with a removal callback that records the removed Guid.
        /// </summary>
        private static (ClientHandler handler, List<Guid> removed) BuildHandler(
            FakeWebSocket socket,
            ISimulationHost host)
        {
            var removed = new List<Guid>();
            var dispatcher = new MessageDispatcher(host);
            var id = Guid.NewGuid();
            var handler = new ClientHandler(id, socket, dispatcher, removedId => removed.Add(removedId));
            return (handler, removed);
        }

        // ─── Tests: normal close (Req 2.2) ────────────────────────────────────

        [Fact]
        public async Task NormalClose_ClientSendsCloseFrame_HandlerCompletesCleanly()
        {
            // Arrange: the fake socket delivers a single Close frame
            var socket = new FakeWebSocket(new[]
            {
                CloseFrame()
            });
            var host = new StubHost();
            var (handler, removed) = BuildHandler(socket, host);

            // Act
            await handler.RunAsync(CancellationToken.None);

            // Assert: RunAsync returns without throwing
            // (if it threw, the await above would propagate the exception)
        }

        [Fact]
        public async Task NormalClose_ClientSendsCloseFrame_HandlerPerformsClosingHandshake()
        {
            // Arrange
            var socket = new FakeWebSocket(new[]
            {
                CloseFrame()
            });
            var host = new StubHost();
            var (handler, removed) = BuildHandler(socket, host);

            // Act
            await handler.RunAsync(CancellationToken.None);

            // Assert: CloseOutputAsync was called with NormalClosure (closing handshake)
            Assert.Equal(1, socket.CloseOutputCallCount);
            Assert.Equal(WebSocketCloseStatus.NormalClosure, socket.LastCloseStatus);
        }

        [Fact]
        public async Task NormalClose_ClientSendsCloseFrame_RemovalCallbackInvoked()
        {
            // Arrange
            var socket = new FakeWebSocket(new[]
            {
                CloseFrame()
            });
            var host = new StubHost();
            var (handler, removed) = BuildHandler(socket, host);

            // Act
            await handler.RunAsync(CancellationToken.None);

            // Assert: the handler removed itself from the connection map (Req 2.2)
            Assert.Single(removed);
            Assert.Equal(handler.Id, removed[0]);
        }

        // ─── Tests: unexpected disconnect (Req 2.3) ───────────────────────────

        [Fact]
        public async Task UnexpectedDisconnect_WebSocketExceptionDuringReceive_HandlerExitsWithoutThrowing()
        {
            // Arrange: fake socket throws on first receive
            var socket = new FakeWebSocket(new[]
            {
                ThrowWebSocketException()
            });
            var host = new StubHost();
            var (handler, removed) = BuildHandler(socket, host);

            // Act – must not propagate the WebSocketException (Req 2.3)
            var ex = await Record.ExceptionAsync(() => handler.RunAsync(CancellationToken.None));

            Assert.Null(ex);
        }

        [Fact]
        public async Task UnexpectedDisconnect_WebSocketExceptionDuringReceive_RemovalCallbackInvoked()
        {
            // Arrange
            var socket = new FakeWebSocket(new[]
            {
                ThrowWebSocketException()
            });
            var host = new StubHost();
            var (handler, removed) = BuildHandler(socket, host);

            // Act
            await handler.RunAsync(CancellationToken.None);

            // Assert: connection is still removed from the map (Req 2.3)
            Assert.Single(removed);
            Assert.Equal(handler.Id, removed[0]);
        }

        [Fact]
        public async Task UnexpectedDisconnect_DoesNotAffectOtherConnections()
        {
            // Arrange: two independent handlers; one crashes, one closes normally
            var crashSocket = new FakeWebSocket(new[]
            {
                ThrowWebSocketException()
            });
            var normalSocket = new FakeWebSocket(new[]
            {
                CloseFrame()
            });
            var host = new StubHost();
            var (crashHandler, crashRemoved) = BuildHandler(crashSocket, host);
            var (normalHandler, normalRemoved) = BuildHandler(normalSocket, host);

            // Act: run both concurrently
            await Task.WhenAll(
                crashHandler.RunAsync(CancellationToken.None),
                normalHandler.RunAsync(CancellationToken.None));

            // Assert: each handler cleaned up independently
            Assert.Single(crashRemoved);
            Assert.Single(normalRemoved);
        }

        // ─── Tests: message passthrough to MessageDispatcher (Req 2.1) ────────

        [Fact]
        public async Task TextFrame_ValidStateRequest_ResponseWrittenBack()
        {
            // Arrange: deliver one state_request, then close
            var socket = new FakeWebSocket(new[]
            {
                TextFrame(@"{""type"":""state_request""}"),
                CloseFrame()
            });
            var host = new StubHost();
            var (handler, _) = BuildHandler(socket, host);

            // Act
            await handler.RunAsync(CancellationToken.None);

            // Assert: at least one message was sent back
            Assert.NotEmpty(socket.SentMessages);

            // The first sent message should be a state_response
            var json = Encoding.UTF8.GetString(socket.SentMessages[0]);
            using var doc = JsonDocument.Parse(json);
            Assert.Equal("state_response", doc.RootElement.GetProperty("type").GetString());
        }

        [Fact]
        public async Task TextFrame_ValidTickRequest_ResponseWrittenBack_AndHostTicked()
        {
            // Arrange: deliver a tick_request for 1 tick, then close
            var socket = new FakeWebSocket(new[]
            {
                TextFrame(@"{""type"":""tick_request"",""count"":1}"),
                CloseFrame()
            });
            var host = new StubHost();
            var (handler, _) = BuildHandler(socket, host);

            // Act
            await handler.RunAsync(CancellationToken.None);

            // Assert: the dispatcher called TickAsync once
            Assert.Equal(1, host.TickCount);

            // The first sent message should be a tick_response
            Assert.NotEmpty(socket.SentMessages);
            var json = Encoding.UTF8.GetString(socket.SentMessages[0]);
            using var doc = JsonDocument.Parse(json);
            Assert.Equal("tick_response", doc.RootElement.GetProperty("type").GetString());
        }

        [Fact]
        public async Task TextFrame_ResponseBytesAreUtf8Json()
        {
            // Arrange
            var socket = new FakeWebSocket(new[]
            {
                TextFrame(@"{""type"":""state_request""}"),
                CloseFrame()
            });
            var host = new StubHost();
            var (handler, _) = BuildHandler(socket, host);

            // Act
            await handler.RunAsync(CancellationToken.None);

            // Assert: the response is valid UTF-8 JSON (Req 5.1)
            Assert.NotEmpty(socket.SentMessages);
            var json = Encoding.UTF8.GetString(socket.SentMessages[0]);
            var ex = Record.Exception(() => JsonDocument.Parse(json));
            Assert.Null(ex);
        }

        [Fact]
        public async Task TextFrame_UnknownMessageType_ErrorResponseWrittenBack()
        {
            // Arrange: deliver an unknown message type, then close
            var socket = new FakeWebSocket(new[]
            {
                TextFrame(@"{""type"":""unknown_message""}"),
                CloseFrame()
            });
            var host = new StubHost();
            var (handler, _) = BuildHandler(socket, host);

            // Act
            await handler.RunAsync(CancellationToken.None);

            // Assert: the dispatcher returned an error_response
            Assert.NotEmpty(socket.SentMessages);
            var json = Encoding.UTF8.GetString(socket.SentMessages[0]);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            Assert.Equal("error_response", root.GetProperty("type").GetString());
            Assert.Equal("unknown_type", root.GetProperty("code").GetString());
        }

        [Fact]
        public async Task RemovalCallback_AlwaysInvoked_EvenAfterNormalRun()
        {
            // Arrange
            var socket = new FakeWebSocket(new[]
            {
                CloseFrame()
            });
            var host = new StubHost();
            var (handler, removed) = BuildHandler(socket, host);

            // Act
            await handler.RunAsync(CancellationToken.None);

            // Assert: finally block runs regardless
            Assert.Single(removed);
        }
    }
}
