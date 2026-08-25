using System;
using HarvestingCore.Configuration;
using HarvestingCore.Coordination;
using HarvestingCore.Pathfinding;
using HarvestingCore.World;

namespace HarvestingCore.Agents
{
    /// <summary>
    /// The narrow, read-mostly window a state gets onto the rest of the world.
    /// Exists so AgentState never needs a back-reference to World, keeping the
    /// layering acyclic. DumpLoad accumulates the discharged total through the
    /// injected discharge sink rather than AgentContext referencing World
    /// directly; World.DischargedTotal is the read-only projection over that sink.
    /// </summary>
    public sealed class AgentContext
    {
        public WorldModel Model { get; }
        public SimulationConfig Config { get; }
        public PathFinder PathFinder { get; }
        public AgentManager Manager { get; }
        public PendingMutations Pending { get; }
        public int TickIndex { get; }

        private readonly Action<int> _dischargeSink;

        public AgentContext(WorldModel model, SimulationConfig config, PathFinder pathFinder,
            AgentManager manager, PendingMutations pending, int tickIndex, Action<int> dischargeSink)
        {
            Model = model ?? throw new ArgumentNullException(nameof(model));
            Config = config ?? throw new ArgumentNullException(nameof(config));
            PathFinder = pathFinder ?? throw new ArgumentNullException(nameof(pathFinder));
            Manager = manager ?? throw new ArgumentNullException(nameof(manager));
            Pending = pending ?? throw new ArgumentNullException(nameof(pending));
            TickIndex = tickIndex;
            _dischargeSink = dischargeSink ?? throw new ArgumentNullException(nameof(dischargeSink));
        }

        /// <summary>Called by Agent.DumpLoad to accumulate the discharged total (Req 6.1, 6.3).</summary>
        internal void AddDischarged(int amount)
        {
            _dischargeSink(amount);
        }
    }
}
