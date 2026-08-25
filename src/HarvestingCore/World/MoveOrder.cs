namespace HarvestingCore.World
{
    /// <summary>
    /// The fixed, ordered list of the eight neighbour offsets (Glossary: Move_Order).
    /// </summary>
    public static class MoveOrder
    {
        public static readonly (int Dx, int Dy)[] Offsets =
        {
            (0, 1), (1, 0), (-1, 0), (0, -1), (-1, 1), (-1, -1), (1, 1), (1, -1)
        };

        public const int Count = 8;
    }
}
