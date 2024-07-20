using System;
using UnityEngine;

namespace TerrainGraph.Util;

public struct Vector2d
{
    public static readonly Vector2d Zero = new(0, 0);
    public static readonly Vector2d AxisX = new(1, 0);
    public static readonly Vector2d AxisZ = new(0, 1);
    public static readonly Vector2d One = new(1, 1);

    public double x;
    public double z;

    public Vector2d(double x, double z)
    {
        this.x = x;
        this.z = z;
    }

    public double Magnitude => Math.Sqrt(x * x + z * z);

    public double SqrMagnitude => x * x + z * z;

    public void Normalize()
    {
        double magnitude = Magnitude;
        if (magnitude > 9.999999747378752E-06)
            this = this / magnitude;
        else
            this = Zero;
    }

    public Vector2d Normalized
    {
        get
        {
            var normalized = new Vector2d(x, z);
            normalized.Normalize();
            return normalized;
        }
    }

    public Vector2d PerpCW => new(z, -x);

    public Vector2d PerpCCW => new(-z, x);

    public readonly bool InBounds(Vector2d minI, Vector2d maxE) => x >= minI.x && x < maxE.x && z >= minI.z && z < maxE.z;

    public static double Dot(Vector2d a, Vector2d b) => a.x * b.x + a.z * b.z;

    public static double PerpDot(Vector2d a, Vector2d b) => a.x * b.z - a.z * b.x;

    public readonly Vector2d Rotate(double sin, double cos) => new(x * cos - z * sin, x * sin + z * cos);

    public override string ToString() => $"[ {x:F2} | {z:F2} ]";

    public override int GetHashCode() => x.GetHashCode() ^ z.GetHashCode() << 2;

    public override bool Equals(object other) => other is Vector2d vec && Equals(vec);

    public readonly bool Equals(Vector2d other) => x == other.x && z == other.z;

    public static Vector2d operator +(Vector2d a, Vector2d b) => new(a.x + b.x, a.z + b.z);

    public static Vector2d operator -(Vector2d a, Vector2d b) => new(a.x - b.x, a.z - b.z);

    public static Vector2d operator *(Vector2d a, Vector2d b) => new(a.x * b.x, a.z * b.z);

    public static Vector2d operator /(Vector2d a, Vector2d b) => new(a.x / b.x, a.z / b.z);

    public static Vector2d operator -(Vector2d a) => new(-a.x, -a.z);

    public static Vector2d operator *(Vector2d a, double d) => new(a.x * d, a.z * d);

    public static Vector2d operator *(double d, Vector2d a) => new(a.x * d, a.z * d);

    public static Vector2d operator /(Vector2d a, double d) => new(a.x / d, a.z / d);

    public static bool operator ==(Vector2d a, Vector2d b)
    {
        double num1 = a.x - b.x;
        double num2 = a.z - b.z;
        return num1 * num1 + num2 * num2 < 9.999999439624929E-11;
    }

    public static bool operator !=(Vector2d a, Vector2d b) => !(a == b);

    public static implicit operator Vector2d(Vector2Int vec) => new(vec.x, vec.y);

    public static implicit operator Vector2d(Vector2 vec) => new(vec.x, vec.y);

    public readonly Vector2Int ToIntRounded() => new((int) Math.Round(x), (int) Math.Round(z));

    public static Vector2d Min(Vector2d a, Vector2d b) => new(Math.Min(a.x, b.x), Math.Min(a.z, b.z));

    public static Vector2d Max(Vector2d a, Vector2d b) => new(Math.Max(a.x, b.x), Math.Max(a.z, b.z));

    public static Vector2d Direction(double angle)
    {
        if (angle == 0) return new Vector2d(1, 0);
        if (angle == 90) return new Vector2d(0, 1);
        if (angle is 180 or -180) return new Vector2d(-1, 0);
        if (angle == -90) return new Vector2d(0, -1);
        return new Vector2d(Math.Cos(angle.ToRad()), Math.Sin(angle.ToRad()));
    }

    public static Vector2d Lerp(Vector2d a, Vector2d b, double t)
    {
        t = t.InRange01();
        return new Vector2d(a.x + (b.x - a.x) * t, a.z + (b.z - a.z) * t);
    }

    public static Vector2d LerpUnclamped(Vector2d a, Vector2d b, double t)
    {
        return new Vector2d(a.x + (b.x - a.x) * t, a.z + (b.z - a.z) * t);
    }

    public static double Angle(Vector2d from, Vector2d to)
    {
        double num = Math.Sqrt(from.SqrMagnitude * to.SqrMagnitude);
        return num < 1.0000000036274937E-15 ? 0.0f : Math.Acos((Dot(from, to) / num).InRange(-1, 1)) * 57.29578;
    }

    public static double SignedAngle(Vector2d from, Vector2d to)
    {
        var dot = from.x * to.z - from.z * to.x;
        return Angle(from, to) * (dot < 0 ? -1 : 1);
    }

    public static double Distance(Vector2d a, Vector2d b)
    {
        double dx = a.x - b.x;
        double dz = a.z - b.z;
        return Math.Sqrt(dx * dx + dz * dz);
    }
    
    public static double DistanceSq(Vector2d a, Vector2d b)
    {
        double dx = a.x - b.x;
        double dz = a.z - b.z;
        return dx * dx + dz * dz;
    }

    /// <summary>
    /// Returns 1 if the point C is on the left of the line from A to B, and -1 if it is on the right.
    /// </summary>
    public static double PointToLineOrientation(Vector2d a, Vector2d b, Vector2d c)
    {
        return Math.Sign((b.x - a.x) * (c.z - a.z) - (b.z - a.z) * (c.x - a.x));
    }

    /// <summary>
    /// Returns the reflection of vector A against a surface with normal N.
    /// </summary>
    public static Vector2d Reflect(Vector2d a, Vector2d n)
    {
        return a - 2 * Dot(a, n) * n;
    }

    public static bool TryIntersect(
        Vector2d originA,
        Vector2d originB,
        Vector2d directionA,
        Vector2d directionB,
        out Vector2d point,
        double limit = 0)
    {
        return TryIntersect(
            originA, originB,
            directionA, directionB,
            out point, out _, limit
        );
    }

    public static bool TryIntersect(
        Vector2d originA,
        Vector2d originB,
        Vector2d directionA,
        Vector2d directionB,
        out Vector2d point,
        out double scalarA,
        double limit = 0)
    {
        var p = PerpDot(directionB, directionA);

        if (Math.Abs(p) <= limit)
        {
            scalarA = 0;
            point = Zero;
            return false;
        }

        scalarA = PerpDot(directionB, originB - originA) / p;
        point = originA + scalarA * directionA;
        return true;
    }
}
