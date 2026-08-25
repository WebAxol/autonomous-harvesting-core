using System;
using System.Collections.Generic;
using HarvestingCore.Agents;

namespace HarvestingCore.Coordination
{
    /// <summary>
    /// Owns the agent collections in registration order, the id lookup dictionary,
    /// and registration itself (Glossary: Agent_Manager). The coordination
    /// methods (assistance requests, tractor selection, meeting point
    /// negotiation, tick execution) land in task 12/13.
    /// </summary>
    public sealed class AgentManager
    {
        private readonly List<Agent> _agents = new List<Agent>();
        private readonly List<Harvester> _harvesters = new List<Harvester>();
        private readonly List<Tractor> _tractors = new List<Tractor>();
        private readonly Dictionary<string, Agent> _byId = new Dictionary<string, Agent>();

        // Assistance_Mapping, maintained as exact inverses. Populated by RequestAssistance/
        // LinkPair/UnlinkPair/ReleasePair (task 13); TryGetPartner/IsPaired below already
        // read them so the FSM transition tables can query pairing state.
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
    }
}
