using System;
using System.Collections.Generic;
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
    /// Unit tests for <see cref="MessageDispatcher"/>.
    /// Requirements: 3.3, 3.4, 3.5, 4.3, 5.2
    /// </summary>
    public class MessageDispatcherTests
    {
        // ─── Fake ISimulationHost implementations ─────────────────────────────

        private sealed class NormalHost : ISimulationHost
        {
            public int TickCount { get; private set; }
            public bool IsHalted { get; set; } = false;
            private int _currentTick;

            public Task TickAsync(CancellationToken ct)
            {
                _currentTick++;
                TickCount++;
                return Task.CompletedTask;
            }

            public SimulationSnapshot GetSnapshot() => new SimulationSnapshot
            {
                Tick = _currentTick,
                IsHalted = IsHalted,
                DischargedTotal = 42,
                Agents = new List<AgentSnapshot>
                {
                    new AgentSnapshot { Id = "a1", Role = "Harvester", State = "Idle", X = 1, Y = 2, Fuel = 100, Load = 0 }
                },
                Cells = new List<CellSnapshot>(),
            };
        }

        private sealed class ThrowingTickHost : ISimulationHost
        {
            public bool IsHalted => false;

            public Task TickAsync(CancellationToken ct) =>
                throw new InvalidOperationException("Tick exploded for testing.");

            public SimulationSnapshot GetSnapshot() => new SimulationSnapshot
            {
                Tick = 0, IsHalted = false, DischargedTotal = 0,
                Agents = new List<AgentSnapshot>(), Cells = new List<CellSnapshot>()
            };
        }

        private sealed class ThrowingSnapshotHost : ISimulationHost
        {
            public bool IsHalted => false;
            public Task TickAsync(CancellationToken ct) => Task.CompletedTask;
            public SimulationSnapshot GetSnapshot() =>
                throw new InvalidOperationException("GetSnapshot exploded for testing.");
        }

        // ─── Helpers ──────────────────────────────────────────────────────────

        private static ReadOnlyMemory<byte> ToPayload(string json) =>
            new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(json));

        private static (string type, string? code) ParseResponse(byte[] bytes)
        {
            var json = Encoding.UTF8.GetString(bytes);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var type = root.GetProperty("type").GetString()!;
            string? code = root.TryGetProperty("code", out var codeProp) ? codeProp.GetString() : null;
            return (type, code);
        }

        private static MessageDispatcher MakeDispatcher(ISimulationHost? host = null) =>
            new MessageDispatcher(host ?? new NormalHost());

        private static Task<byte[]> Dispatch(MessageDispatcher d, string json) =>
            d.HandleAsync(ToPayload(json), _ => Task.CompletedTask, CancellationToken.None);

        // ─── Tests: unknown_type ──────────────────────────────────────────────

        [Fact]
        public async Task UnknownType_ReturnsErrorResponse_WithCode_UnknownType()
        {
            var dispatcher = MakeDispatcher();
            var response = await Dispatch(dispatcher, @"{""type"":""ping""}");
            var (type, code) = ParseResponse(response);
            Assert.Equal("error_response", type);
            Assert.Equal("unknown_type", code);
        }

        [Fact]
        public async Task MissingTypeField_ReturnsErrorResponse_WithCode_UnknownType()
        {
            var dispatcher = MakeDispatcher();
            var response = await Dispatch(dispatcher, @"{""count"":1}");
            var (type, code) = ParseResponse(response);
            Assert.Equal("error_response", type);
            Assert.Equal("unknown_type", code);
        }

        // ─── Tests: invalid_count ─────────────────────────────────────────────

        [Fact]
        public async Task TickRequest_CountZero_ReturnsErrorResponse_WithCode_InvalidCount()
        {
            var dispatcher = MakeDispatcher();
            var response = await Dispatch(dispatcher, @"{""type"":""tick_request"",""count"":0}");
            var (type, code) = ParseResponse(response);
            Assert.Equal("error_response", type);
            Assert.Equal("invalid_count", code);
        }

        [Fact]
        public async Task TickRequest_NegativeCount_ReturnsErrorResponse_WithCode_InvalidCount()
        {
            var dispatcher = MakeDispatcher();
            var response = await Dispatch(dispatcher, @"{""type"":""tick_request"",""count"":-1}");
            var (type, code) = ParseResponse(response);
            Assert.Equal("error_response", type);
            Assert.Equal("invalid_count", code);
        }

        // ─── Tests: simulation_halted ─────────────────────────────────────────

        [Fact]
        public async Task TickRequest_WhenIsHalted_ReturnsErrorResponse_WithCode_SimulationHalted()
        {
            var host = new NormalHost { IsHalted = true };
            var dispatcher = MakeDispatcher(host);
            var response = await Dispatch(dispatcher, @"{""type"":""tick_request"",""count"":1}");
            var (type, code) = ParseResponse(response);
            Assert.Equal("error_response", type);
            Assert.Equal("simulation_halted", code);
        }

        [Fact]
        public async Task TickRequest_WhenIsHalted_DoesNotCallTickAsync()
        {
            var host = new NormalHost { IsHalted = true };
            var dispatcher = MakeDispatcher(host);
            await Dispatch(dispatcher, @"{""type"":""tick_request"",""count"":1}");
            Assert.Equal(0, host.TickCount);
        }

        // ─── Tests: tick_error ────────────────────────────────────────────────

        [Fact]
        public async Task TickRequest_WhenTickAsyncThrows_ReturnsErrorResponse_WithCode_TickError()
        {
            var host = new ThrowingTickHost();
            var dispatcher = MakeDispatcher(host);
            var response = await Dispatch(dispatcher, @"{""type"":""tick_request"",""count"":1}");
            var (type, code) = ParseResponse(response);
            Assert.Equal("error_response", type);
            Assert.Equal("tick_error", code);
        }

        // ─── Tests: snapshot_error ────────────────────────────────────────────

        [Fact]
        public async Task StateRequest_WhenGetSnapshotThrows_ReturnsErrorResponse_WithCode_SnapshotError()
        {
            var host = new ThrowingSnapshotHost();
            var dispatcher = MakeDispatcher(host);
            var response = await Dispatch(dispatcher, @"{""type"":""state_request""}");
            var (type, code) = ParseResponse(response);
            Assert.Equal("error_response", type);
            Assert.Equal("snapshot_error", code);
        }

        // ─── Tests: happy-path tick ───────────────────────────────────────────

        [Fact]
        public async Task TickRequest_Count1_ReturnsTickResponse_WithCorrectSnapshot()
        {
            var host = new NormalHost();
            var dispatcher = MakeDispatcher(host);
            var response = await Dispatch(dispatcher, @"{""type"":""tick_request"",""count"":1}");

            var json = Encoding.UTF8.GetString(response);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.Equal("tick_response", root.GetProperty("type").GetString());
            Assert.True(root.TryGetProperty("snapshot", out var snapshotProp));
            Assert.Equal(JsonValueKind.Object, snapshotProp.ValueKind);
            // After 1 tick, tick counter should be 1
            Assert.Equal(1, root.GetProperty("tick").GetInt32());
        }

        [Fact]
        public async Task TickRequest_Count3_SendsIntermediateResponses_AndReturnsFinalResponse()
        {
            var host = new NormalHost();
            var dispatcher = MakeDispatcher(host);
            var intermediateResponses = new List<byte[]>();

            var finalResponse = await dispatcher.HandleAsync(
                ToPayload(@"{""type"":""tick_request"",""count"":3}"),
                bytes => { intermediateResponses.Add(bytes); return Task.CompletedTask; },
                CancellationToken.None);

            // 2 intermediate + 1 final = 3 total
            Assert.Equal(2, intermediateResponses.Count);
            Assert.Equal(3, host.TickCount);

            // Final response should be tick_response
            var (type, _) = ParseResponse(finalResponse);
            Assert.Equal("tick_response", type);

            // Final response should have tick=3
            var json = Encoding.UTF8.GetString(finalResponse);
            using var doc = JsonDocument.Parse(json);
            Assert.Equal(3, doc.RootElement.GetProperty("tick").GetInt32());
        }

        // ─── Tests: happy-path state ──────────────────────────────────────────

        [Fact]
        public async Task StateRequest_ReturnsStateResponse_WithCorrectSnapshot()
        {
            var host = new NormalHost();
            var dispatcher = MakeDispatcher(host);
            var response = await Dispatch(dispatcher, @"{""type"":""state_request""}");

            var json = Encoding.UTF8.GetString(response);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.Equal("state_response", root.GetProperty("type").GetString());
            Assert.True(root.TryGetProperty("snapshot", out var snapshotProp));
            Assert.Equal(JsonValueKind.Object, snapshotProp.ValueKind);
        }

        [Fact]
        public async Task StateRequest_DoesNotAdvanceTick()
        {
            var host = new NormalHost();
            var dispatcher = MakeDispatcher(host);
            await Dispatch(dispatcher, @"{""type"":""state_request""}");
            Assert.Equal(0, host.TickCount);
        }
    }
}
