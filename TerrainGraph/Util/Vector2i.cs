using System;
using UnityEngine;

namespace TerrainGraph.Util;

public struct Vector2i
{
    public static readonly Vector2i Zero = new(0, 0);
    public static readonly Vector2i AxisX = new(1, 0);
    public static readonly Vector2i AxisZ = new(0, 1);
    public static readonly Vector2i One = new(1, 1);

    public int x;
    public int z;

    public Vector2i(int v)
    {
        this.x = v;
        this.z = v;
    }

    public Vector2i(int x, int z)
    {
        this.x = x;
        this.z = z;
    }

    public Vector2i PerpCW => new(z, -x);

    public Vector2i PerpCCW => new(-z, x);

    public readonly bool InBounds(Vector2i minI, Vector2i maxE) => x >= minI.x && x < maxE.x && z >= minI.z && z < maxE.z;

    public override string ToString() => $"[ {x:F2} | {z:F2} ]";

    public override int GetHashCode() => x.GetHashCode() ^ z.GetHashCode() << 2;

    public override bool Equals(object other) => other is Vector2i vec && Equals(vec);

    public readonly bool Equals(Vector2i other) => x == other.x && z == other.z;

    public static Vector2i operator +(Vector2i a, Vector2i b) => new(a.x + b.x, a.z + b.z);

    public static Vector2i operator -(Vector2i a, Vector2i b) => new(a.x - b.x, a.z - b.z);

    public static Vector2i operator *(Vector2i a, Vector2i b) => new(a.x * b.x, a.z * b.z);

    public static Vector2i operator /(Vector2i a, Vector2i b) => new(a.x / b.x, a.z / b.z);

    public static Vector2i operator -(Vector2i a) => new(-a.x, -a.z);

    public static Vector2i operator *(Vector2i a, int d) => new(a.x * d, a.z * d);

    public static Vector2i operator *(int d, Vector2i a) => new(a.x * d, a.z * d);

    public static Vector2i operator /(Vector2i a, int d) => new(a.x / d, a.z / d);

    public static bool operator ==(Vector2i a, Vector2i b) => a.x == b.x && a.z == b.z;

    public static bool operator !=(Vector2i a, Vector2i b) => !(a == b);

    public static implicit operator Vector2i(Vector2Int vec) => new(vec.x, vec.y);

    public static Vector2i Min(Vector2i a, Vector2i b) => new(Math.Min(a.x, b.x), Math.Min(a.z, b.z));

    public static Vector2i Max(Vector2i a, Vector2i b) => new(Math.Max(a.x, b.x), Math.Max(a.z, b.z));

    public static double Distance(Vector2i a, Vector2i b)
    {
        double dx = a.x - b.x;
        double dz = a.z - b.z;
        return Math.Sqrt(dx * dx + dz * dz);
    }

    public static double DistanceSq(Vector2i a, Vector2i b)
    {
        double dx = a.x - b.x;
        double dz = a.z - b.z;
        return dx * dx + dz * dz;
    }
}
