using System;

namespace HarvestingCore.Configuration
{
    /// <summary>
    /// Xorshift128+ pseudo-random generator, pinned in-source rather than using
    /// System.Random. System.Random's algorithm is not contractually stable across
    /// .NET versions, which would break seed-determinism requirements the moment
    /// Unity's runtime differs from the test host.
    /// </summary>
    public sealed class DeterministicRandom : IRandomSource
    {
        private ulong _state0;
        private ulong _state1;

        public int Seed { get; }

        public DeterministicRandom(int seed)
        {
            Seed = seed;
            // SplitMix64 to seed the two xorshift128+ state words from a single int seed.
            ulong z = unchecked((ulong)seed + 0x9E3779B97F4A7C15UL);
            _state0 = SplitMix64(ref z);
            _state1 = SplitMix64(ref z);
            if (_state0 == 0 && _state1 == 0)
            {
                _state0 = 1;
            }
        }

        private static ulong SplitMix64(ref ulong state)
        {
            state = unchecked(state + 0x9E3779B97F4A7C15UL);
            ulong result = state;
            result = unchecked((result ^ (result >> 30)) * 0xBF58476D1CE4E5B9UL);
            result = unchecked((result ^ (result >> 27)) * 0x94D049BB133111EBUL);
            return result ^ (result >> 31);
        }

        private ulong NextUInt64()
        {
            ulong s1 = _state0;
            ulong s0 = _state1;
            _state0 = s0;
            s1 ^= s1 << 23;
            s1 ^= s1 >> 17;
            s1 ^= s0;
            s1 ^= s0 >> 26;
            _state1 = s1;
            return unchecked(_state0 + _state1);
        }

        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
            {
                throw new ArgumentOutOfRangeException(nameof(maxExclusive),
                    "maxExclusive must be greater than minInclusive.");
            }

            ulong range = (ulong)((long)maxExclusive - (long)minInclusive);
            ulong value = NextUInt64() % range;
            return (int)((long)minInclusive + (long)value);
        }

        public double NextDouble()
        {
            // Top 53 bits give a double in [0, 1) with full mantissa precision.
            ulong bits = NextUInt64() >> 11;
            return bits * (1.0 / (1UL << 53));
        }

        public IRandomSource Fork(int salt)
        {
            unchecked
            {
                int forkedSeed = Seed * 397 + salt;
                return new DeterministicRandom(forkedSeed);
            }
        }
    }
}
