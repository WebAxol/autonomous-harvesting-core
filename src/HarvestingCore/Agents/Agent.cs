using System;
using System.Collections.Generic;
using HarvestingCore.Agents.States;
using HarvestingCore.Configuration;
using HarvestingCore.World;

namespace HarvestingCore.Agents
{
    /// <summary>
    /// The abstract base component holding identifier, position, fuel, load,
    /// capacity limits, fuel consumption, current path, and the state machine
    /// (Glossary: Agent). Execute and the TransitionTable hook land in task 11
    /// alongside the FSM tables.
    /// </summary>
    public abstract class Agent
    {
        private readonly List<GridPosition> _path = new List<GridPosition>();

        public string Id { get; }
        public int RegistrationIndex { get; internal set; }
        public GridPosition Position { get; private set; }
        public int Fuel { get; private set; }
        public int Load { get; private set; }
        public int MaxLoad { get; }
        public int MaxFuel { get; }
        public int FuelConsumption { get; }
        public StateId CurrentState { get; private set; }
        public IReadOnlyList<GridPosition> Path { get; }
        public GridPosition? MeetingPoint { get; internal set; }
        public bool PathInvalidatedThisTick { get; private set; }
        public bool ArrivedAtDestination { get; private set; }
        public int InactiveSinceTick { get; private set; }
        public abstract AgentRole Role { get; }

        internal bool RefuelledThisTick { get; private set; }
        internal bool DumpedThisTick { get; private set; }

        /// <summary>Set by AgentManager.ResolveTransfers (task 13) on both sides of a
        /// completed load transfer. Read by the WAIT_TRACTOR/WAIT_HARVESTER transition
        /// rows instead of re-deriving completion from Load/Position. Cleared by the
        /// wait states' OnExit, since the flag is set one phase (tick N's Phase 2) before
        /// the Execute call that must observe it (tick N+1's Phase 1) and must not be
        /// cleared by the top-of-Execute reset in between.</summary>
        internal bool TransferCompletedThisTick { get; private set; }

        protected Agent(string id, GridPosition start, WorldModel model, SimulationConfig config,
            int? maxLoad = null, int? maxFuel = null, int? fuelConsumption = null)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("id must not be null, empty, or whitespace.", nameof(id));
            }
            if (model == null)
            {
                throw new ArgumentNullException(nameof(model));
            }
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            int resolvedMaxLoad = maxLoad ?? config.DefaultMaxLoad;
            int resolvedMaxFuel = maxFuel ?? config.DefaultMaxFuel;
            int resolvedFuelConsumption = fuelConsumption ?? config.DefaultFuelConsumption;

            if (resolvedMaxLoad < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxLoad), "maxLoad must be at least 1.");
            }
            if (resolvedMaxFuel < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxFuel), "maxFuel must be at least 1.");
            }
            if (resolvedFuelConsumption < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(fuelConsumption), "fuelConsumption must be at least 1.");
            }
            if (!model.InBounds(start))
            {
                throw new ArgumentException("start position " + start + " is out of bounds.", nameof(start));
            }
            if (model.CellAt(start).State == CellState.Blocked)
            {
                throw new ArgumentException("start position " + start + " is Blocked.", nameof(start));
            }

            Id = id;
            Position = start;
            MaxLoad = resolvedMaxLoad;
            MaxFuel = resolvedMaxFuel;
            FuelConsumption = resolvedFuelConsumption;
            Fuel = resolvedMaxFuel;
            Load = 0;
            CurrentState = StateId.Idle;
            Path = _path.AsReadOnly();
            MeetingPoint = null;
            PathInvalidatedThisTick = false;
            ArrivedAtDestination = false;
            InactiveSinceTick = -1;
        }

        /// <summary>Req 3.5, 3.6: no-op when transitioning to the current state,
        /// otherwise OnExit, set state, OnEnter, in that order.</summary>
        public void Transition(StateId next, AgentContext context)
        {
            if (next == CurrentState)
            {
                return;
            }
            AgentStateRegistry.Get(CurrentState).OnExit(this, context);
            CurrentState = next;
            AgentStateRegistry.Get(next).OnEnter(this, context);
        }

        /// <summary>Req 4.1 - 4.6: advances one step along the current path.</summary>
        public GridPosition Move(AgentContext context)
        {
            if (_path.Count == 0)
            {
                return Position;
            }

            GridPosition next = _path[0];
            Cell targetCell = context.Model.CellAt(next);

            if (targetCell.State == CellState.Blocked)
            {
                _path.Clear();
                PathInvalidatedThisTick = true;
                return Position;
            }

            if (!next.IsNeighbourOf(Position))
            {
                // Malformed path (should not occur with PathFinder-produced paths);
                // reject without mutating position, fuel, or the path (Req 4.4).
                return Position;
            }

            _path.RemoveAt(0);
            Position = next;
            SetFuel(Fuel - FuelConsumption);
            targetCell.RegisterEntry();

            if (_path.Count == 0)
            {
                ArrivedAtDestination = true;
            }

            return Position;
        }

        /// <summary>Req 5.1, 5.2: succeeds only when standing on a refuel station.</summary>
        public bool Refuel(AgentContext context)
        {
            if (!IsAtAnyOf(context.Model.RefuelStations))
            {
                return false;
            }
            SetFuel(MaxFuel);
            RefuelledThisTick = true;
            return true;
        }

        /// <summary>Req 5.3, 5.4: nearest-station path cost times FuelConsumption.
        /// False when no station is reachable or the collection is empty.</summary>
        public bool TryEstimateFuelReserve(AgentContext context, out int reserve)
        {
            reserve = 0;
            if (context.Model.RefuelStations.Count == 0)
            {
                return false;
            }
            if (!context.PathFinder.TryCostToNearest(Position, context.Model.RefuelStations, out _, out int cost))
            {
                return false;
            }
            reserve = cost * FuelConsumption;
            return true;
        }

        /// <summary>Req 6.1, 6.2: succeeds only when standing on a dump site.</summary>
        public bool DumpLoad(AgentContext context)
        {
            if (!IsAtAnyOf(context.Model.DumpSites))
            {
                return false;
            }
            context.AddDischarged(Load);
            SetLoad(0);
            DumpedThisTick = true;
            return true;
        }

        /// <summary>Req 9.10: accepts min(offered, free capacity), returns the accepted amount.</summary>
        public int ReceiveLoad(int offered)
        {
            int freeCapacity = MaxLoad - Load;
            int accepted = Math.Min(offered, freeCapacity);
            if (accepted < 0)
            {
                accepted = 0;
            }
            SetLoad(Load + accepted);
            return accepted;
        }

        /// <summary>Decreases load by amount (clamped at zero), returns the amount actually removed.</summary>
        public int RemoveLoad(int amount)
        {
            int before = Load;
            SetLoad(Load - amount);
            return before - Load;
        }

        public void SetPath(IReadOnlyList<GridPosition> path)
        {
            _path.Clear();
            if (path != null)
            {
                _path.AddRange(path);
            }
        }

        public void ClearPath()
        {
            _path.Clear();
        }

        /// <summary>Clamps to [0, MaxFuel] (Req 3.8).</summary>
        internal void SetFuel(int value)
        {
            Fuel = Clamp(value, 0, MaxFuel);
        }

        /// <summary>Clamps to [0, MaxLoad] (Req 3.7).</summary>
        internal void SetLoad(int value)
        {
            Load = Clamp(value, 0, MaxLoad);
        }

        /// <summary>Records the tick at which INACTIVE was entered (Req 15.1).</summary>
        internal void SetInactiveSinceTick(int tickIndex)
        {
            InactiveSinceTick = tickIndex;
        }

        /// <summary>Set by AgentManager.ResolveTransfers (task 13) on both sides of a
        /// completed load transfer, so the WAIT_TRACTOR/WAIT_HARVESTER transition rows
        /// can read the result of an action that happened in a prior tick's Phase 2
        /// (Req 8.9, 9.6, 9.7). Consumed and cleared by the wait states' OnExit.</summary>
        internal void MarkTransferCompleted()
        {
            TransferCompletedThisTick = true;
        }

        /// <summary>Clears the transfer-completion flag once the wait state has been
        /// exited via the transition it caused.</summary>
        internal void ClearTransferCompleted()
        {
            TransferCompletedThisTick = false;
        }

        /// <summary>Clears the per-tick action flags; called at the top of Execute.</summary>
        internal void ResetPerTickFlags()
        {
            PathInvalidatedThisTick = false;
            ArrivedAtDestination = false;
            RefuelledThisTick = false;
            DumpedThisTick = false;
        }

        /// <summary>Req 3.3: the current state's Execute exactly once, then the
        /// pre-emptive fuel guard and the role's transition table (Req 8.12, 8.13,
        /// 9.9, 9.11, 15.1). See remarks on the fuel guard ordering in the design.</summary>
        public void Execute(AgentContext context)
        {
            ResetPerTickFlags();

            if (CurrentState != StateId.Inactive && Fuel <= 0)
            {
                Transition(StateId.Inactive, context);
                return;
            }
            if (CurrentState == StateId.Inactive)
            {
                return;
            }

            AgentStateRegistry.Get(CurrentState).Execute(this, context);

            if (Fuel <= 0)
            {
                Transition(StateId.Inactive, context);
                return;
            }

            if (TransitionTable.Evaluate(this, context, out StateId next))
            {
                Transition(next, context);
            }
        }

        /// <summary>The role-specific, priority-ordered rule set (Req 8.13, 9.11).</summary>
        protected abstract TransitionTable TransitionTable { get; }

        private bool IsAtAnyOf(IReadOnlyList<GridPosition> positions)
        {
            for (int i = 0; i < positions.Count; i++)
            {
                if (positions[i].Equals(Position))
                {
                    return true;
                }
            }
            return false;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }
            if (value > max)
            {
                return max;
            }
            return value;
        }
    }
}
