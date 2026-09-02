using System.Collections.Generic;
using System.Text;
using FsCheck;
using FsCheck.Xunit;
using HarvestingCore.Transport;
using HarvestingCore.Transport.Dto;
using Xunit;

namespace HarvestingCore.Transport.Tests
{
    /// <summary>
    /// Property-based tests for <see cref="SnapshotSerializer"/>.
    /// Validates: Requirements 7.3
    /// </summary>
    public class SnapshotSerializerPropertyTests
    {
        // ─── Arbitrary generators ──────────────────────────────────────────────

        private static Arbitrary<string> NonNullString() =>
            Arb.Default.String()
               .Filter(s => s != null)
               .Convert(s => s!, s => s);

        private static Gen<AgentSnapshot> GenAgent()
        {
            return from id in Arb.Generate<NonEmptyString>().Select(s => s.Get)
                   from role in Gen.Elements("Harvester", "Tractor")
                   from state in Gen.Elements("Idle", "Moving", "Harvesting", "Returning", "Refueling")
                   from x in Arb.Generate<int>()
                   from y in Arb.Generate<int>()
                   from fuel in Arb.Generate<int>()
                   from load in Arb.Generate<int>()
                   select new AgentSnapshot
                   {
                       Id = id,
                       Role = role,
                       State = state,
                       X = x,
                       Y = y,
                       Fuel = fuel,
                       Load = load,
                   };
        }

        private static Gen<CellSnapshot> GenCell()
        {
            return from x in Arb.Generate<int>()
                   from y in Arb.Generate<int>()
                   from state in Gen.Elements("Empty", "Crop", "Harvested")
                   from ownerId in Gen.OneOf(
                       Gen.Constant<string?>(null),
                       Arb.Generate<NonEmptyString>().Select<NonEmptyString, string?>(s => s.Get))
                   select new CellSnapshot
                   {
                       X = x,
                       Y = y,
                       State = state,
                       OwnerId = ownerId,
                   };
        }

        private static Gen<List<T>> GenListOf<T>(Gen<T> gen) =>
            Gen.ListOf(gen).Select(arr => new List<T>(arr));

        private static Gen<SimulationSnapshot> GenSnapshot()
        {
            return from tick in Arb.Generate<int>()
                   from isHalted in Arb.Generate<bool>()
                   from discharged in Arb.Generate<int>()
                   from agents in GenListOf(GenAgent())
                   from cells in GenListOf(GenCell())
                   select new SimulationSnapshot
                   {
                       Tick = tick,
                       IsHalted = isHalted,
                       DischargedTotal = discharged,
                       Agents = agents,
                       Cells = cells,
                   };
        }

        public static Arbitrary<SimulationSnapshot> ArbitrarySnapshot() =>
            GenSnapshot().ToArbitrary();

        // ─── Structural equality helpers ────────────────────────────────────────

        private static bool AgentsEqual(AgentSnapshot a, AgentSnapshot b) =>
            a.Id == b.Id &&
            a.Role == b.Role &&
            a.State == b.State &&
            a.X == b.X &&
            a.Y == b.Y &&
            a.Fuel == b.Fuel &&
            a.Load == b.Load;

        private static bool CellsEqual(CellSnapshot a, CellSnapshot b) =>
            a.X == b.X &&
            a.Y == b.Y &&
            a.State == b.State &&
            a.OwnerId == b.OwnerId;

        private static bool SnapshotsEqual(SimulationSnapshot a, SimulationSnapshot b)
        {
            if (a.Tick != b.Tick) return false;
            if (a.IsHalted != b.IsHalted) return false;
            if (a.DischargedTotal != b.DischargedTotal) return false;
            if (a.Agents.Count != b.Agents.Count) return false;
            if (a.Cells.Count != b.Cells.Count) return false;

            for (int i = 0; i < a.Agents.Count; i++)
                if (!AgentsEqual(a.Agents[i], b.Agents[i])) return false;

            for (int i = 0; i < a.Cells.Count; i++)
                if (!CellsEqual(a.Cells[i], b.Cells[i])) return false;

            return true;
        }

        // ─── Property 1: round-trip consistency ─────────────────────────────────

        /// <summary>
        /// Property 1: For all structurally valid <see cref="SimulationSnapshot"/> instances,
        /// Deserialize(Serialize(snapshot)) produces an object structurally equal to the original.
        /// Validates: Requirements 7.3
        /// </summary>
        [Property(Arbitrary = new[] { typeof(SnapshotSerializerPropertyTests) })]
        public Property RoundTrip_Serialize_Deserialize_ProducesStructurallyEqualSnapshot(SimulationSnapshot original)
        {
            var bytes = SnapshotSerializer.Serialize(original);
            var json = Encoding.UTF8.GetString(bytes);
            var deserialized = SnapshotSerializer.Deserialize(json, out var error);

            return (error == null && deserialized != null && SnapshotsEqual(original, deserialized))
                .Label("Round-trip must preserve all fields; error=" + (error ?? "none"));
        }
    }
}
