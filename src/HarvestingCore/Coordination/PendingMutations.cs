using System.Collections.Generic;

namespace HarvestingCore.Coordination
{
    /// <summary>
    /// Cross-agent effects deferred from Phase 1 (agent execution) to Phase 2/3 of
    /// World.Tick, so a tick's outcome is independent of intra-tick observation
    /// order (Req 16.2).
    /// </summary>
    public sealed class PendingMutations
    {
        private readonly List<string> _transferReadyAgentIds = new List<string>();
        private readonly HashSet<string> _transferReadySeen = new HashSet<string>();

        private readonly List<string> _assistanceCleanupAgentIds = new List<string>();
        private readonly HashSet<string> _assistanceCleanupSeen = new HashSet<string>();

        public IReadOnlyList<string> TransferReadyAgentIds { get; }
        public IReadOnlyList<string> AssistanceCleanupAgentIds { get; }
        public bool RedistributionRequested { get; private set; }

        public PendingMutations()
        {
            TransferReadyAgentIds = _transferReadyAgentIds.AsReadOnly();
            AssistanceCleanupAgentIds = _assistanceCleanupAgentIds.AsReadOnly();
        }

        /// <summary>Deduplicated, insertion-ordered. WaitTractorState/WaitHarvesterState
        /// call this from OnEnter so the transfer is resolved after all agents run.</summary>
        public void EnqueueTransferReady(Agents.Agent agent)
        {
            if (agent == null)
            {
                return;
            }
            if (_transferReadySeen.Add(agent.Id))
            {
                _transferReadyAgentIds.Add(agent.Id);
            }
        }

        /// <summary>Deduplicated, insertion-ordered. InactiveState.OnEnter calls this
        /// so pair teardown happens after all agents run.</summary>
        public void EnqueueAssistanceCleanup(Agents.Agent agent)
        {
            if (agent == null)
            {
                return;
            }
            if (_assistanceCleanupSeen.Add(agent.Id))
            {
                _assistanceCleanupAgentIds.Add(agent.Id);
            }
        }

        public void RequestRedistribution()
        {
            RedistributionRequested = true;
        }

        public void Clear()
        {
            _transferReadyAgentIds.Clear();
            _transferReadySeen.Clear();
            _assistanceCleanupAgentIds.Clear();
            _assistanceCleanupSeen.Clear();
            RedistributionRequested = false;
        }
    }
}
