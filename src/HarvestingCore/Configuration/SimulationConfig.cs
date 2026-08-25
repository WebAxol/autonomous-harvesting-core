using System;
using HarvestingCore.World;

namespace HarvestingCore.Configuration
{
    /// <summary>
    /// Immutable, injectable value object supplying all tunable constants to
    /// Harvesting_Core (Req 17). Only the members needed by the world model layer
    /// are populated so far; remaining members land alongside the layers that use
    /// them (pathfinding, agents, coordination).
    /// </summary>
    public sealed class SimulationConfig
    {
        public double DumpPreferenceFactor { get; }
        public double CapacityFactor { get; }
        public double HarvesterFuelReserveMultiplier { get; }
        public double TractorFuelReserveMultiplier { get; }
        public int CropCost { get; }
        public int EmptyCost { get; }
        public int HarvestedCost { get; }
        public HeuristicKind Heuristic { get; }
        public int DefaultMaxLoad { get; }
        public int DefaultMaxFuel { get; }
        public int DefaultFuelConsumption { get; }
        public int Seed { get; }
        public double CropDensity { get; }
        public double BlockedDensity { get; }

        public static SimulationConfig Default { get; } = new SimulationConfig();

        public SimulationConfig(
            double dumpPreferenceFactor = 1.0,
            double capacityFactor = 0.5,
            double harvesterFuelReserveMultiplier = 1.2,
            double tractorFuelReserveMultiplier = 2.5,
            int cropCost = 1,
            int emptyCost = 2,
            int harvestedCost = 10,
            HeuristicKind heuristic = HeuristicKind.Octile,
            int defaultMaxLoad = 100,
            int defaultMaxFuel = 1000,
            int defaultFuelConsumption = 1,
            int seed = 20240101,
            double cropDensity = 0.55,
            double blockedDensity = 0.10)
        {
            if (capacityFactor < 0.0 || capacityFactor > 1.0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacityFactor), "capacityFactor must be within [0, 1].");
            }
            if (dumpPreferenceFactor < 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(dumpPreferenceFactor), "dumpPreferenceFactor must not be negative.");
            }
            if (harvesterFuelReserveMultiplier < 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(harvesterFuelReserveMultiplier), "harvesterFuelReserveMultiplier must not be negative.");
            }
            if (tractorFuelReserveMultiplier < 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(tractorFuelReserveMultiplier), "tractorFuelReserveMultiplier must not be negative.");
            }
            if (cropCost < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(cropCost), "cropCost must be at least 1.");
            }
            if (emptyCost < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(emptyCost), "emptyCost must be at least 1.");
            }
            if (harvestedCost < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(harvestedCost), "harvestedCost must be at least 1.");
            }
            if (defaultMaxLoad < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(defaultMaxLoad), "defaultMaxLoad must be at least 1.");
            }
            if (defaultMaxFuel < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(defaultMaxFuel), "defaultMaxFuel must be at least 1.");
            }
            if (defaultFuelConsumption < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(defaultFuelConsumption), "defaultFuelConsumption must be at least 1.");
            }
            if (cropDensity < 0.0 || cropDensity > 1.0)
            {
                throw new ArgumentOutOfRangeException(nameof(cropDensity), "cropDensity must be within [0, 1].");
            }
            if (blockedDensity < 0.0 || blockedDensity > 1.0)
            {
                throw new ArgumentOutOfRangeException(nameof(blockedDensity), "blockedDensity must be within [0, 1].");
            }
            if (cropDensity + blockedDensity > 1.0)
            {
                throw new ArgumentOutOfRangeException(nameof(blockedDensity), "cropDensity plus blockedDensity must not exceed 1.");
            }

            DumpPreferenceFactor = dumpPreferenceFactor;
            CapacityFactor = capacityFactor;
            HarvesterFuelReserveMultiplier = harvesterFuelReserveMultiplier;
            TractorFuelReserveMultiplier = tractorFuelReserveMultiplier;
            CropCost = cropCost;
            EmptyCost = emptyCost;
            HarvestedCost = harvestedCost;
            Heuristic = heuristic;
            DefaultMaxLoad = defaultMaxLoad;
            DefaultMaxFuel = defaultMaxFuel;
            DefaultFuelConsumption = defaultFuelConsumption;
            Seed = seed;
            CropDensity = cropDensity;
            BlockedDensity = blockedDensity;
        }

        public int MinimumTerrainCost => Math.Min(CropCost, Math.Min(EmptyCost, HarvestedCost));

        /// <summary>Terrain cost of entering a cell in the given state. Blocked cells are
        /// never entered, so callers must filter them out before calling this.</summary>
        public int TerrainCost(CellState state)
        {
            switch (state)
            {
                case CellState.Crop:
                    return CropCost;
                case CellState.Empty:
                    return EmptyCost;
                case CellState.Harvested:
                    return HarvestedCost;
                default:
                    throw new ArgumentOutOfRangeException(nameof(state), "Blocked cells have no terrain cost.");
            }
        }
    }
}
