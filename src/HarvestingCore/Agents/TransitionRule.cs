using System;

namespace HarvestingCore.Agents
{
    /// <summary>
    /// One row of a role's transition table: a source state, a pure guard
    /// predicate, and the target state to apply when the guard holds. The
    /// <see cref="RequirementRef"/> documents which acceptance criterion the
    /// row encodes (e.g. "8.4").
    /// </summary>
    public readonly struct TransitionRule
    {
        public StateId Source { get; }
        public StateId Target { get; }
        public Func<Agent, AgentContext, bool> Guard { get; }
        public string RequirementRef { get; }

        public TransitionRule(StateId source, StateId target, Func<Agent, AgentContext, bool> guard,
            string requirementRef)
        {
            Source = source;
            Target = target;
            Guard = guard ?? throw new ArgumentNullException(nameof(guard));
            RequirementRef = requirementRef;
        }
    }
}
