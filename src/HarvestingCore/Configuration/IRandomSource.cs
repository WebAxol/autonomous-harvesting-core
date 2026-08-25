namespace HarvestingCore.Configuration
{
    /// <summary>
    /// Injectable, seeded pseudo-random source. Grid generation is the only consumer
    /// inside Harvesting_Core; agent decision making is fully deterministic.
    /// </summary>
    public interface IRandomSource
    {
        int Seed { get; }

        /// <summary>Returns an int in [minInclusive, maxExclusive).</summary>
        int NextInt(int minInclusive, int maxExclusive);

        /// <summary>Returns a double in [0.0, 1.0).</summary>
        double NextDouble();

        /// <summary>Independent stream, still a pure function of (Seed, salt).</summary>
        IRandomSource Fork(int salt);
    }
}
