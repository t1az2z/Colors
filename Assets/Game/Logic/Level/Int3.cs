using System;

namespace ColorsAction.Level
{
    /// <summary>Целочисленная клетка воксельной сетки (x, y — вверх, z). Без зависимостей от движка.</summary>
    public readonly struct Int3 : IEquatable<Int3>
    {
        public readonly int X;
        public readonly int Y;
        public readonly int Z;

        public Int3(int x, int y, int z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static readonly Int3 Zero = new Int3(0, 0, 0);
        public static readonly Int3 Up = new Int3(0, 1, 0);
        public static readonly Int3 Down = new Int3(0, -1, 0);

        public Int3 Above => new Int3(X, Y + 1, Z);
        public Int3 Below => new Int3(X, Y - 1, Z);

        public static Int3 operator +(Int3 a, Int3 b) => new Int3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Int3 operator -(Int3 a, Int3 b) => new Int3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static bool operator ==(Int3 a, Int3 b) => a.Equals(b);
        public static bool operator !=(Int3 a, Int3 b) => !a.Equals(b);

        public bool Equals(Int3 other) => X == other.X && Y == other.Y && Z == other.Z;
        public override bool Equals(object obj) => obj is Int3 other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var h = X * 73856093;
                h ^= Y * 19349663;
                h ^= Z * 83492791;
                return h;
            }
        }

        public override string ToString() => $"{X},{Y},{Z}";

        /// <summary>Разбор "x,y,z".</summary>
        public static Int3 Parse(string s)
        {
            var parts = s.Split(',');
            if (parts.Length != 3)
                throw new FormatException($"cell '{s}' must be 'x,y,z'");
            return new Int3(int.Parse(parts[0].Trim()), int.Parse(parts[1].Trim()), int.Parse(parts[2].Trim()));
        }
    }
}
