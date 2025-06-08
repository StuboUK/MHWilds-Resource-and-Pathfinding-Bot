using System;

namespace MHWildsPathfindingBot.Core.Models
{
    /// <summary>
    /// 2D vector structure for position and direction
    /// </summary>
    public struct Vector2 : IEquatable<Vector2>
    {
        public float X; public float Z;

        public Vector2(float x, float z) { X = x; Z = z; }

        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.X - b.X, a.Z - b.Z);
        public static Vector2 operator /(Vector2 a, float scalar) => new Vector2(a.X / scalar, a.Z / scalar);
        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.X + b.X, a.Z + b.Z);
        public static Vector2 operator *(Vector2 a, float scalar) => new Vector2(a.X * scalar, a.Z * scalar);

        public float Magnitude() => (float)Math.Sqrt(X * X + Z * Z);

        public Vector2 Normalized()
        {
            float mag = Magnitude();
            return mag > 0 ? this / mag : new Vector2(0, 0);
        }

        public static float DotProduct(Vector2 a, Vector2 b) => a.X * b.X + a.Z * b.Z;

        public (int, int) ToGridCoordinates()
        {
            int gridX = (int)((X - Globals.OriginX) / Globals.CellSize);
            int gridZ = (int)((Z - Globals.OriginZ) / Globals.CellSize);

            // Add logging to understand conversion
            Console.WriteLine($"World X: {X}, World Z: {Z}");
            Console.WriteLine($"Origin X: {Globals.OriginX}, Origin Z: {Globals.OriginZ}");
            Console.WriteLine($"Calculated Grid X: {gridX}, Grid Z: {gridZ}");

            return (gridX, gridZ);
        }
        public bool Equals(Vector2 other) => X == other.X && Z == other.Z;

        public override bool Equals(object obj) => obj is Vector2 other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(X, Z);

        public override string ToString() => $"({X:F1}, {Z:F1})";
    }
}