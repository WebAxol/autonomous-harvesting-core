using System;

namespace HarvestingCore.World
{
    /// <summary>
    /// A grid coordinate. X is the column index, Y is the row index, origin (0,0)
    /// at the top-left; the cell matrix is indexed matrix[y][x] (Assumption 1).
    /// </summary>
    public readonly struct GridPosition : IEquatable<GridPosition>
    {
        public int X { get; }
        public int Y { get; }

        public GridPosition(int x, int y)
        {
            X = x;
            Y = y;
        }

        public GridPosition Offset(int dx, int dy)
        {
            return new GridPosition(X + dx, Y + dy);
        }

        /// <summary>True when this position is exactly one Move_Order offset away from other.</summary>
        public bool IsNeighbourOf(GridPosition other)
        {
            int dx = X - other.X;
            int dy = Y - other.Y;
            var offsets = MoveOrder.Offsets;
            for (int i = 0; i < offsets.Length; i++)
            {
                if (offsets[i].Dx == dx && offsets[i].Dy == dy)
                {
                    return true;
                }
            }
            return false;
        }

        public bool Equals(GridPosition other)
        {
            return X == other.X && Y == other.Y;
        }

        public override bool Equals(object obj)
        {
            return obj is GridPosition other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (X * 397) ^ Y;
            }
        }

        public override string ToString()
        {
            return "(" + X.ToString() + ", " + Y.ToString() + ")";
        }

        public static bool operator ==(GridPosition left, GridPosition right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(GridPosition left, GridPosition right)
        {
            return !left.Equals(right);
        }

        /// <summary>Row-major ordering: by y then by x (Req 11.2).</summary>
        public static int CompareRowMajor(GridPosition a, GridPosition b)
        {
            int byY = a.Y.CompareTo(b.Y);
            return byY != 0 ? byY : a.X.CompareTo(b.X);
        }
    }
}
