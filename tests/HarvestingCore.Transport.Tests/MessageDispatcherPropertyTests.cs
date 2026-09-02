using System;
using System.Collections.Generic;
using System.Text;
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
    /// Property-based tests for <see cref="MessageDispatcher"/>.
    /// </summary>
    public class MessageDispatcherPropertyTests
    {
        // ─── Fake host that tracks concurrent TickAsync calls ─────────────────

        private sealed class ConcurrencyTrackingHost : ISimulationHost
        {
            private int _currentConcurrency;
            public int MaxConcurrencyObserved;

            public bool IsHalted => false;

            public Task TickAsync(CancellationToken ct)
            {
                int current = Interlocked.Increment(ref _currentConcurrency);

                // Record maximum observed concurrency using a lock-free compare-and-swap loop
                int observed;
                do
                {
                    observed = Volatile.Read(ref MaxConcurrencyObserved);
                    if (current <= observed) break;
                }
                while (Interlocked.CompareExchange(ref MaxConcurrencyObserved, current, observed) != observed);

                // Simulate a small delay so concurrent calls can overlap
                Thread.SpinWait(500);

                Interlocked.Decrement(ref _currentConcurrency);
                return Task.CompletedTask;
            }

            public SimulationSnapshot GetSnapshot() => new SimulationSnapshot
            {
                Tick = 1,
                IsHalted = false,
                DischargedTotal = 0,
                Agents = new List<AgentSnapshot>(),
                Cells = new List<CellSnapshot>(),
            };
        }

        // ─── Helper: build a tick_request payload with count=1 ───────────────

        private static ReadOnlyMemory<byte> MakeTickRequestPayload(int count = 1)
        {
            var json = $"{{\"type\":\"tick_request\",\"count\":{count}}}";
            return new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(json));
        }

        // ─── Property 2: at-most-one-tick-at-a-time ───────────────────────────

        /// <summary>
        /// Property 2: When N concurrent HandleAsync (tick_request, count=1) calls are issued,
        /// ISimulationHost.TickAsync is never called more than once simultaneously.
        /// Validates: Requirements 8.1, 8.4
        /// </summary>
        [Property]
        public Property TickLock_AtMostOneTickAtATime_WhenNConcurrentRequests()
        {
            // Generate N in [2, 10]
            var genN = Gen.Choose(2, 10);

            return Prop.ForAll(genN.ToArbitrary(), n =>
            {
                var host = new ConcurrencyTrackingHost();
                var dispatcher = new MessageDispatcher(host);
                var payload = MakeTickRequestPayload(count: 1);
                var tasks = new Task[n];

                for (int i = 0; i < n; i++)
                {
                    tasks[i] = dispatcher.HandleAsync(
                        payload,
                        _ => Task.CompletedTask,
                        CancellationToken.None);
                }

                Task.WaitAll(tasks);

                return (host.MaxConcurrencyObserved == 1)
                    .Label($"MaxConcurrency={host.MaxConcurrencyObserved}, expected 1; N={n}");
            });
        }
    }
}
