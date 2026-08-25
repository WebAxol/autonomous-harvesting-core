using System;
using System.Collections.Generic;
using HarvestingCore.Agents;
using HarvestingCore.Configuration;
using HarvestingCore.Coordination;
using HarvestingCore.Pathfinding;
using HarvestingCore.World;

namespace HarvestingCore
{
    /// <summary>
    /// The top-level façade component that owns a WorldModel and an AgentManager
    /// and exposes the tick entry point plus coordination operations
    /// (Glossary: World). This is the only mutation entry point for a host: a
    /// Unity adapter reads Cells/Agents/TickIndex and calls Tick() from whatever
    /// loop it likes.
    ///
    /// Named SimulationWorld rather than World: a type cannot share its simple
    /// name with a sibling namespace, and HarvestingCore.World is already the
    /// namespace for WorldModel, Cell, GridPosition, etc. (CS0101).
    /// </summary>
    public sealed class SimulationWorld
    {
        private readonly AreaDistributor _areaDistributor = new AreaDistributor();
        private readonly PendingMutations _pending = new PendingMutations();

        public WorldModel Model { get; }
        public AgentManager Manager { get; }
        public PathFinder PathFinder { get; }
        public SimulationConfig Config { get; }
        public IRandomSource Random { get; }

        public int TickIndex { get; private set; }
        public int DischargedTotal { get; private set; }
        public bool IsHalted => Manager.AllInactive();
        public IReadOnlyList<Agent> Agents => Manager.Agents;
        public IReadOnlyList<Cell> Cells => Model.Cells;

        public SimulationWorld(WorldModel model, SimulationConfig config, IRandomSource random)
        {
            Model = model ?? throw new ArgumentNullException(nameof(model));
            Config = config ?? throw new ArgumentNullException(nameof(config));
            Random = random ?? throw new ArgumentNullException(nameof(random));

            Manager = new AgentManager();
            PathFinder = new PathFinder(Model, Config);
            TickIndex = 0;
            DischargedTotal = 0;
        }

        /// <summary>Req 16.3 - 16.5: appends the agent to the registration-ordered
        /// collections and assigns its initial IDLE state.</summary>
        public void Register(Agent agent)
        {
            Manager.Register(agent);
        }

        /// <summary>Req 1.6, 1.7, 1.8: populates the grid once, using this world's
        /// injected Random and Config.</summary>
        public bool GenerateGrid()
        {
            return Model.Generate(Random, Config);
        }

        /// <summary>Req 12.1 - 12.5, 12.9: runs area distribution immediately,
        /// outside of the Tick pipeline (e.g. for initial setup).</summary>
        public void RedistributeAreas()
        {
            _areaDistributor.Distribute(Model, Manager.Harvesters);
        }

        /// <summary>The four-phase tick pipeline (Req 16.1, 16.2). One AgentContext
        /// is built up front so every agent observes the same TickIndex.</summary>
        public void Tick()
        {
            var ctx = new AgentContext(Model, Config, PathFinder, Manager, _pending, TickIndex, AddDischarged);

            // Phase 1: every registered agent executes exactly once, in registration
            // order (Req 16.1). Cross-agent effects are recorded in _pending only.
            IReadOnlyList<Agent> agents = Manager.Agents;
            for (int i = 0; i < agents.Count; i++)
            {
                agents[i].Execute(ctx);

                // Req 12.6: a harvester whose owned area has nothing left to harvest
                // triggers redistribution at the end of the current tick.
                if (agents[i] is Harvester harvester
                    && harvester.CurrentState != StateId.Inactive
                    && harvester.IsAreaFinished(ctx))
                {
                    _pending.RequestRedistribution();
                }
            }

            // Phase 2: cross-agent effects, in enqueue order. Cleanup runs before
            // transfers so a pair whose member went inactive this tick cannot
            // transfer this tick (Req 10.7, 10.8, 16.2).
            Manager.ResolveAssistanceCleanup(_pending, ctx);
            Manager.ResolveTransfers(_pending, ctx);

            // Phase 3: redistribution runs at most once, only when requested
            // (Req 12.6, 12.7).
            if (_pending.RedistributionRequested)
            {
                _areaDistributor.Distribute(Model, Manager.Harvesters);
            }

            // Phase 4: clear pending mutations, then advance the tick index so
            // everything within this tick observed the same TickIndex (Req 16.2, 18.2).
            _pending.Clear();
            TickIndex++;
        }

        /// <summary>Backs the discharge sink AgentContext writes through from
        /// Agent.DumpLoad, so AgentContext never needs a reference to this façade
        /// (Req 6.1, 6.3).</summary>
        internal void AddDischarged(int amount)
        {
            DischargedTotal += amount;
        }
    }
}
