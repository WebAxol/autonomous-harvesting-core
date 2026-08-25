namespace HarvestingCore.World
{
    /// <summary>
    /// The unit of the grid, holding a Cell_State, a popularity counter, and an
    /// owner identifier (Glossary: Cell).
    /// </summary>
    public sealed class Cell
    {
        public const string NoOwner = "";

        public CellState State { get; private set; }
        public int Popularity { get; private set; }
        public string OwnerId { get; private set; }

        public Cell()
        {
            State = CellState.Empty;
            Popularity = 0;
            OwnerId = NoOwner;
        }

        /// <summary>Crop -> Harvested (Req 2.1, 2.2).</summary>
        public bool Harvest()
        {
            if (State != CellState.Crop)
            {
                return false;
            }
            State = CellState.Harvested;
            return true;
        }

        /// <summary>Empty|Harvested -> Crop (Req 2.3, 2.4).</summary>
        public bool Plant()
        {
            if (State != CellState.Empty && State != CellState.Harvested)
            {
                return false;
            }
            State = CellState.Crop;
            return true;
        }

        /// <summary>Req 2.5: ownership is reported for the assigned identifier only.</summary>
        public bool IsOwnedBy(string agentId)
        {
            return OwnerId != NoOwner && OwnerId == agentId;
        }

        public void AssignOwner(string agentId)
        {
            OwnerId = agentId;
        }

        /// <summary>Req 12.5: clears an owner assigned by a previous distribution.</summary>
        public void ClearOwner()
        {
            OwnerId = NoOwner;
        }

        /// <summary>Increments popularity by exactly one and returns the updated value (Req 2.6).</summary>
        public int RegisterEntry()
        {
            Popularity++;
            return Popularity;
        }

        /// <summary>Used only by grid generation and parsing; bypasses the Harvest/Plant rules.</summary>
        internal void SetStateForGeneration(CellState state)
        {
            State = state;
        }
    }
}
