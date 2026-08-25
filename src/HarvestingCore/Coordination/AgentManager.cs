using System;
using System.Collections.Generic;
using HarvestingCore.Agents;
using HarvestingCore.Pathfinding;
using HarvestingCore.World;

namespace HarvestingCore.Coordination
{
    /// <summary>
    /// Owns the agent collections in registration order, the id lookup dictionary,
    /// registration itself, and the coordination operations: assistance requests,
    /// tractor selection, meeting point negotiation, transfer/cleanup resolution,
    /// and tick execution (Glossary: Agent_Manager).
    /// </summary>
    public sealed class AgentManager
    {
        private readonly List<Agent> _agents = new List<Agent>();
        private readonly List<Harvester> _harvesters = new List<Harvester>();
        private readonly List<Tractor> _tractors = new List<Tractor>();
        private readonly Dictionary<string, Agent> _byId = new Dictionary<string, Agent>();

        // Assistance_Mapping, maintained as exact inverses. Mutated only through
        // LinkPair/UnlinkPair so the two dictionaries never drift apart (Req 10.2, 10.3).
        private readonly Dictionary<string, string> _tractorToHarvester = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _harvesterToTractor = new Dictionary<string, string>();

        public IReadOnlyList<Agent> Agents { get; }
        public IReadOnlyList<Harvester> Harvesters { get; }
        public IReadOnlyList<Tractor> Tractors { get; }

        public AgentManager()
        {
            Agents = _agents.AsReadOnly();
            Harvesters = _harvesters.AsReadOnly();
            Tractors = _tractors.AsReadOnly();
        }

        /// <summary>
        /// Appends the agent to the registration-ordered collections and assigns
        /// its RegistrationIndex and initial IDLE state (Req 16.3, 16.4). Rejects
        /// a duplicate id with InvalidOperationException naming the id, and a
        /// null agent with ArgumentNullException (Req 16.5).
        /// </summary>
        public void Register(Agent agent)
        {
            if (agent == null)
            {
                throw new ArgumentNullException(nameof(agent));
            }
            if (_byId.ContainsKey(agent.Id))
            {
                throw new InvalidOperationException("duplicate agent identifier '" + agent.Id + "'");
            }

            // The agent's CurrentState already defaults to IDLE from its constructor
            // (Req 16.3, 16.4); registration only stamps the ordering key.
            agent.RegistrationIndex = _agents.Count;

            _agents.Add(agent);
            _byId.Add(agent.Id, agent);

            switch (agent.Role)
            {
                case AgentRole.Harvester:
                    _harvesters.Add((Harvester)agent);
                    break;
                case AgentRole.Tractor:
                    _tractors.Add((Tractor)agent);
                    break;
            }
        }

        public bool TryGetAgent(string id, out Agent agent)
        {
            return _byId.TryGetValue(id, out agent);
        }

        /// <summary>Assistance_Mapping lookup: true when the agent (harvester or
        /// tractor) currently holds a paired partner.</summary>
        public bool IsPaired(Agent agent)
        {
            if (agent == null)
            {
                return false;
            }
            return agent.Role == AgentRole.Harvester
                ? _harvesterToTractor.ContainsKey(agent.Id)
                : _tractorToHarvester.ContainsKey(agent.Id);
        }

        /// <summary>Resolves the paired partner for a harvester or tractor, if any.
        /// Read by the FSM transition tables (rows referencing "partner lost").</summary>
        public bool TryGetPartner(Agent agent, out Agent partner)
        {
            partner = null;
            if (agent == null)
            {
                return false;
            }

            string partnerId;
            if (agent.Role == AgentRole.Harvester)
            {
                if (!_harvesterToTractor.TryGetValue(agent.Id, out partnerId))
                {
                    return false;
                }
            }
            else
            {
                if (!_tractorToHarvester.TryGetValue(agent.Id, out partnerId))
                {
                    return false;
                }
            }

            return _byId.TryGetValue(partnerId, out partner);
        }

        /// <summary>Req 15.6: true when every registered agent holds INACTIVE.</summary>
        public bool AllInactive()
        {
            for (int i = 0; i < _agents.Count; i++)
            {
                if (_agents[i].CurrentState != StateId.Inactive)
                {
                    return false;
                }
            }
            return _agents.Count > 0;
        }

        /// <summary>
        /// Scans tractors in registration order, keeping only IDLE, unpaired,
        /// non-INACTIVE candidates that the supplied cost field reaches; picks the
        /// minimum cost, tie-breaking on the lowest id in ordinal order (Req 10.1,
        /// 10.4, 10.5, 15.4).
        /// </summary>
        public bool TrySelectTractor(Harvester harvester, CostField harvesterField, out Tractor best)
        {
            best = null;
            int bestCost = CostField.Unreachable;

            for (int i = 0; i < _tractors.Count; i++)
            {
                Tractor candidate = _tractors[i];
                if (candidate.CurrentState != StateId.Idle)
                {
                    continue;
                }
                if (_tractorToHarvester.ContainsKey(candidate.Id))
                {
                    continue;
                }

                int candidateIndex = harvesterField.Width * candidate.Position.Y + candidate.Position.X;
                if (!harvesterField.IsReachable(candidateIndex))
                {
                    continue;
                }

                int cost = harvesterField.CostAt(candidateIndex);
                if (best == null || cost < bestCost ||
                    (cost == bestCost && string.CompareOrdinal(candidate.Id, best.Id) < 0))
                {
                    bestCost = cost;
                    best = candidate;
                }
            }

            return best != null;
        }

        /// <summary>
        /// Short-circuits to the harvester position when Load == MaxLoad (Req 11.5).
        /// Otherwise computes both cost fields once and scans cells in row-major
        /// order, skipping Blocked and jointly unreachable cells, taking the strict
        /// minimum of the summed cost so the first (lowest y, then lowest x) minimum
        /// wins (Req 11.1, 11.2, 11.3, 11.4).
        /// </summary>
        public bool TryNegotiateMeetingPoint(Harvester harvester, Tractor tractor, AgentContext ctx,
            out GridPosition meetingPoint)
        {
            if (harvester.Load == harvester.MaxLoad)
            {
                meetingPoint = harvester.Position;
                return true;
            }

            CostField harvesterField = ctx.PathFinder.ComputeCostField(harvester.Position);
            CostField tractorField = ctx.PathFinder.ComputeCostField(tractor.Position);

            IReadOnlyList<Cell> cells = ctx.Model.Cells;
            int bestIndex = -1;
            int bestCombined = int.MaxValue;

            for (int i = 0; i < cells.Count; i++)
            {
                if (cells[i].State == CellState.Blocked)
                {
                    continue;
                }
                if (!harvesterField.IsReachable(i) || !tractorField.IsReachable(i))
                {
                    continue;
                }

                int combined = harvesterField.CostAt(i) + tractorField.CostAt(i);
                if (combined < bestCombined)
                {
                    bestCombined = combined;
                    bestIndex = i;
                }
            }

            if (bestIndex < 0)
            {
                meetingPoint = default;
                return false;
            }

            meetingPoint = ctx.Model.PositionOf(bestIndex);
            return true;
        }

        /// <summary>
        /// Composes ComputeCostField, TrySelectTractor and TryNegotiateMeetingPoint.
        /// Records exactly one pair on success and leaves the mapping untouched on
        /// failure; on negotiation failure releases any pre-existing pair for the
        /// harvester (Req 10.1 - 10.5, 11.1 - 11.5).
        /// </summary>
        public bool RequestAssistance(Harvester harvester, AgentContext ctx,
            out Tractor tractor, out GridPosition meetingPoint)
        {
            tractor = null;
            meetingPoint = default;

            CostField harvesterField = ctx.PathFinder.ComputeCostField(harvester.Position);
            if (!TrySelectTractor(harvester, harvesterField, out Tractor selected))
            {
                return false;
            }

            if (!TryNegotiateMeetingPoint(harvester, selected, ctx, out GridPosition negotiated))
            {
                if (_harvesterToTractor.TryGetValue(harvester.Id, out string existingTractorId))
                {
                    UnlinkPair(existingTractorId, harvester.Id);
                }
                return false;
            }

            LinkPair(selected.Id, harvester.Id);
            selected.AssignedHarvesterId = harvester.Id;
            selected.MeetingPoint = negotiated;
            harvester.MeetingPoint = negotiated;

            tractor = selected;
            meetingPoint = negotiated;
            return true;
        }

        /// <summary>Req 10.6: removes the Assistance_Mapping entry for a completed pair.</summary>
        public void ReleasePair(string harvesterId, string tractorId)
        {
            UnlinkPair(tractorId, harvesterId);
        }

        /// <summary>
        /// Fires only when the two agents are paired, co-located at the negotiated
        /// meeting point, and hold WAIT_TRACTOR / WAIT_HARVESTER. Uses the single
        /// ReceiveLoad return value for both sides, marks the per-tick transfer
        /// flags, then releases the pair (Req 9.10, 10.8, 16.2).
        /// </summary>
        public void ResolveTransfers(PendingMutations pending, AgentContext ctx)
        {
            IReadOnlyList<string> ready = pending.TransferReadyAgentIds;
            for (int i = 0; i < ready.Count; i++)
            {
                if (!TryGetAgent(ready[i], out Agent agent))
                {
                    continue;
                }

                Harvester harvester;
                Tractor tractor;
                if (agent.Role == AgentRole.Harvester)
                {
                    harvester = (Harvester)agent;
                    if (!TryGetPartner(harvester, out Agent partner))
                    {
                        continue;
                    }
                    tractor = (Tractor)partner;
                }
                else
                {
                    tractor = (Tractor)agent;
                    if (!TryGetPartner(tractor, out Agent partner))
                    {
                        continue;
                    }
                    harvester = (Harvester)partner;
                }

                if (harvester.CurrentState != StateId.WaitTractor || tractor.CurrentState != StateId.WaitHarvester)
                {
                    continue;
                }
                if (!harvester.MeetingPoint.HasValue || !harvester.Position.Equals(harvester.MeetingPoint.Value))
                {
                    continue;
                }
                if (!tractor.Position.Equals(harvester.Position))
                {
                    continue;
                }

                int accepted = tractor.ReceiveLoad(harvester.Load);
                harvester.RemoveLoad(accepted);
                harvester.MarkTransferCompleted();
                tractor.MarkTransferCompleted();
                ReleasePair(harvester.Id, tractor.Id);
            }
        }

        /// <summary>
        /// Drains assistance-cleanup requests in insertion order, releasing the pair
        /// and forcing a still-active partner to IDLE with MeetingPoint and
        /// AssignedHarvesterId reset, while leaving an already-inactive partner
        /// untouched (Req 10.7, 15.4).
        /// </summary>
        public void ResolveAssistanceCleanup(PendingMutations pending, AgentContext ctx)
        {
            IReadOnlyList<string> cleanup = pending.AssistanceCleanupAgentIds;
            for (int i = 0; i < cleanup.Count; i++)
            {
                if (!TryGetAgent(cleanup[i], out Agent inactive))
                {
                    continue;
                }
                if (!TryGetPartner(inactive, out Agent partner))
                {
                    continue;
                }

                if (inactive.Role == AgentRole.Harvester)
                {
                    ReleasePair(inactive.Id, partner.Id);
                }
                else
                {
                    ReleasePair(partner.Id, inactive.Id);
                }

                if (partner.CurrentState != StateId.Inactive)
                {
                    partner.MeetingPoint = null;
                    if (partner is Tractor partnerTractor)
                    {
                        partnerTractor.AssignedHarvesterId = null;
                    }
                    partner.Transition(StateId.Idle, ctx);
                }
            }
        }

        /// <summary>Req 16.1: invokes each registered agent's Execute exactly once, in
        /// registration order.</summary>
        public void ExecuteTick(AgentContext ctx)
        {
            for (int i = 0; i < _agents.Count; i++)
            {
                _agents[i].Execute(ctx);
            }
        }

        /// <summary>Writes or erases both sides of the Assistance_Mapping together, so
        /// the two dictionaries stay exact inverses (Req 10.2, 10.3).</summary>
        private void LinkPair(string tractorId, string harvesterId)
        {
            _tractorToHarvester[tractorId] = harvesterId;
            _harvesterToTractor[harvesterId] = tractorId;
        }

        private void UnlinkPair(string tractorId, string harvesterId)
        {
            _tractorToHarvester.Remove(tractorId);
            _harvesterToTractor.Remove(harvesterId);
        }
    }
}
