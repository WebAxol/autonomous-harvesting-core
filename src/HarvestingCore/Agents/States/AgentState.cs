namespace HarvestingCore.Agents.States
{
    /// <summary>
    /// The abstract behaviour component for one State_Id, exposing OnEnter,
    /// Execute, and OnExit operations (Glossary: Agent_State). Concrete states
    /// are stateless singletons held by AgentStateRegistry.
    /// </summary>
    public abstract class AgentState
    {
        public abstract StateId Id { get; }

        public virtual void OnEnter(Agent agent, AgentContext context)
        {
        }

        public abstract void Execute(Agent agent, AgentContext context);

        public virtual void OnExit(Agent agent, AgentContext context)
        {
        }
    }
}
