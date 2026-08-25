namespace HarvestingCore.Agents
{
    /// <summary>
    /// Distinguishes the two Agent subtypes for role-specific lookups
    /// (transition table selection, coordination collections).
    /// </summary>
    public enum AgentRole
    {
        Harvester,
        Tractor
    }
}
