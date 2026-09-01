using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using HarvestingCore.Transport;
using HarvestingCore.Transport.Dto;
using Xunit;

namespace HarvestingCore.Transport.Tests
{
    /// <summary>
    /// Unit tests for <see cref="SnapshotSerializer"/>.
    /// Requirements: 5.1, 7.4
    /// </summary>
    public class SnapshotSerializerTests
    {
        // ─── Helpers ────────────────────────────────────────────────────────────

        private static SimulationSnapshot MakeSampleSnapshot() => new SimulationSnapshot
        {
            Tick = 42,
            IsHalted = false,
            DischargedTotal = 100,
            Agents = new List<AgentSnapshot>
            {
                new AgentSnapshot
                {
                    Id = "agent-1",
                    Role = "Harvester",
                    State = "Idle",
                    X = 3,
                    Y = 7,
                    Fuel = 80,
                    Load = 20,
                }
            },
            Cells = new List<CellSnapshot>
            {
                new CellSnapshot { X = 0, Y = 0, State = "Crop", OwnerId = null },
            }
        };

        // ─── Test 1: Serialize produces valid UTF-8 JSON bytes ──────────────────

        [Fact]
        public void Serialize_ProducesValidUtf8JsonBytes()
        {
            var snapshot = MakeSampleSnapshot();

            var bytes = SnapshotSerializer.Serialize(snapshot);

            Assert.NotNull(bytes);
            Assert.NotEmpty(bytes);

            // Must be valid UTF-8
            var json = Encoding.UTF8.GetString(bytes);
            Assert.False(string.IsNullOrWhiteSpace(json));

            // Must be parseable JSON
            using var doc = JsonDocument.Parse(json);
            Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
        }

        // ─── Test 2: Serialized JSON uses camelCase field names ─────────────────

        [Fact]
        public void Serialize_UsesCamelCaseFieldNames()
        {
            var snapshot = MakeSampleSnapshot();

            var bytes = SnapshotSerializer.Serialize(snapshot);
            var json = Encoding.UTF8.GetString(bytes);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Top-level camelCase fields
            Assert.True(root.TryGetProperty("isHalted", out _), "Expected 'isHalted' (camelCase)");
            Assert.True(root.TryGetProperty("dischargedTotal", out _), "Expected 'dischargedTotal' (camelCase)");
            Assert.True(root.TryGetProperty("tick", out _), "Expected 'tick'");
            Assert.True(root.TryGetProperty("agents", out _), "Expected 'agents'");
            Assert.True(root.TryGetProperty("cells", out _), "Expected 'cells'");
        }

        // ─── Test 3: Malformed JSON returns null with a non-null error (does not throw) ──

        [Fact]
        public void Deserialize_MalformedJson_ReturnsNullWithError()
        {
            var malformedJson = "{ this is not valid json!!!";

            var result = SnapshotSerializer.Deserialize(malformedJson, out var error);

            Assert.Null(result);
            Assert.NotNull(error);
            Assert.NotEmpty(error);
        }

        [Fact]
        public void Deserialize_EmptyString_ReturnsNullWithError()
        {
            var result = SnapshotSerializer.Deserialize(string.Empty, out var error);

            Assert.Null(result);
            Assert.NotNull(error);
        }

        // ─── Test 4: Valid round-trip ────────────────────────────────────────────

        [Fact]
        public void Deserialize_AfterSerialize_ProducesEqualSnapshot()
        {
            var original = MakeSampleSnapshot();

            var bytes = SnapshotSerializer.Serialize(original);
            var json = Encoding.UTF8.GetString(bytes);
            var result = SnapshotSerializer.Deserialize(json, out var error);

            Assert.Null(error);
            Assert.NotNull(result);

            Assert.Equal(original.Tick, result.Tick);
            Assert.Equal(original.IsHalted, result.IsHalted);
            Assert.Equal(original.DischargedTotal, result.DischargedTotal);

            Assert.Equal(original.Agents.Count, result.Agents.Count);
            var origAgent = original.Agents[0];
            var resAgent = result.Agents[0];
            Assert.Equal(origAgent.Id, resAgent.Id);
            Assert.Equal(origAgent.Role, resAgent.Role);
            Assert.Equal(origAgent.State, resAgent.State);
            Assert.Equal(origAgent.X, resAgent.X);
            Assert.Equal(origAgent.Y, resAgent.Y);
            Assert.Equal(origAgent.Fuel, resAgent.Fuel);
            Assert.Equal(origAgent.Load, resAgent.Load);

            Assert.Equal(original.Cells.Count, result.Cells.Count);
            var origCell = original.Cells[0];
            var resCell = result.Cells[0];
            Assert.Equal(origCell.X, resCell.X);
            Assert.Equal(origCell.Y, resCell.Y);
            Assert.Equal(origCell.State, resCell.State);
            Assert.Equal(origCell.OwnerId, resCell.OwnerId);
        }

        // ─── Test 5: Empty agents/cells lists serialize and deserialize correctly ──

        [Fact]
        public void Serialize_EmptyAgentsAndCells_ProducesEmptyArrays()
        {
            var snapshot = new SimulationSnapshot
            {
                Tick = 1,
                IsHalted = true,
                DischargedTotal = 0,
                Agents = new List<AgentSnapshot>(),
                Cells = new List<CellSnapshot>(),
            };

            var bytes = SnapshotSerializer.Serialize(snapshot);
            var json = Encoding.UTF8.GetString(bytes);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            Assert.Equal(JsonValueKind.Array, root.GetProperty("agents").ValueKind);
            Assert.Equal(0, root.GetProperty("agents").GetArrayLength());
            Assert.Equal(JsonValueKind.Array, root.GetProperty("cells").ValueKind);
            Assert.Equal(0, root.GetProperty("cells").GetArrayLength());
        }

        [Fact]
        public void Deserialize_EmptyAgentsAndCells_ProducesEmptyLists()
        {
            var snapshot = new SimulationSnapshot
            {
                Tick = 5,
                IsHalted = false,
                DischargedTotal = 0,
                Agents = new List<AgentSnapshot>(),
                Cells = new List<CellSnapshot>(),
            };

            var bytes = SnapshotSerializer.Serialize(snapshot);
            var json = Encoding.UTF8.GetString(bytes);
            var result = SnapshotSerializer.Deserialize(json, out var error);

            Assert.Null(error);
            Assert.NotNull(result);
            Assert.Empty(result.Agents);
            Assert.Empty(result.Cells);
        }
    }
}
