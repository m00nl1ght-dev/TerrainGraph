using System;

namespace TerrainGraph.Util;

public struct Vector2d
{
    public static readonly Vector2d Zero = new(0, 0);
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

    public static double Dot(Vector2d a, Vector2d b) => a.x * b.x + a.z * b.z;

    public static double PerpDot(Vector2d a, Vector2d b) => a.x * b.z - a.z * b.x;

    public override string ToString() => $"[ {x} | {z} ]";

    public override int GetHashCode() => x.GetHashCode() ^ z.GetHashCode() << 2;

    public override bool Equals(object other) => other is Vector2d vec && Equals(vec);

    public bool Equals(Vector2d other) => x == other.x && z == other.z;

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
        return Angle(from, to) * Math.Sign(from.x * to.z - from.z * to.x);
    }

    public static double Distance(Vector2d a, Vector2d b)
    {
        double dx = a.x - b.x;
        double dz = a.z - b.z;
        return Math.Sqrt(dx * dx + dz * dz);
    }

    public static bool TryIntersect(
        Vector2d originA,
        Vector2d originB,
        Vector2d directionA,
        Vector2d directionB,
        out Vector2d point,
        double limit = 0)
    {
        var p = PerpDot(directionB, directionA);

        if (Math.Abs(p) <= limit)
        {
            point = Zero;
            return false;
        }

        var s = PerpDot(directionB, originB - originA) / p;
        point = originA + s * directionA;
        return true;
    }
}
