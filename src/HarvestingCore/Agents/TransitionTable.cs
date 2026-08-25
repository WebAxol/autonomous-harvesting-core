using System;

namespace HarvestingCore.Agents
{
    /// <summary>
    /// One flat, priority-ordered array of <see cref="TransitionRule"/> for a
    /// single role. Array index is the priority index (Req 8.13, 9.11):
    /// <see cref="Evaluate"/> returns the target of the first rule whose
    /// source matches the agent's current state and whose guard holds, so at
    /// most one transition happens per tick.
    /// </summary>
    public sealed class TransitionTable
    {
        private readonly TransitionRule[] _rules;

        public TransitionTable(TransitionRule[] rules)
        {
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        }

        public bool Evaluate(Agent agent, AgentContext ctx, out StateId next)
        {
            for (int i = 0; i < _rules.Length; i++)
            {
                TransitionRule rule = _rules[i];
                if (rule.Source != agent.CurrentState)
                {
                    continue;
                }
                if (!rule.Guard(agent, ctx))
                {
                    continue;
                }
                next = rule.Target;
                return true;
            }
            next = agent.CurrentState;
            return false;
        }
    }
}
