using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TerrainGraph.Util;

public static class MathUtil
{
    public delegate T Interpolation<T>(double t, T a, T b);

    public static readonly Func<double, double> Identity = a => a;

    public static long Factorial(long v) => v <= 1 ? 1 : v * Factorial(v - 1);

    public static long Combination(long a, long b) => a <= 1 ? 1 : Factorial(a) / (Factorial(b) * Factorial(a - b));

    public static double BinomialDist(int n, int x, double p = 0.5) => Combination(n, x) * Math.Pow(p, x) * Math.Pow(1 - p, n - x);

    public static double LinearDist(int n, int x)
    {
        double nh = Math.Floor(n / 2d);
        double sum = (nh + n % 2) * (nh + 1);
        double val = x < nh ? x + 1 : n - x;
        return val / sum;
    }

    public static double NormalizeDeg(this double value)
    {
        value %= 360;
        if (value > 180) return value - 360;
        if (value <= -180) return value + 360;
        return value;
    }

    public static double AngleDeltaAbs(double a, double b)
    {
        var angleDelta = Math.Abs(a - b);
        if (angleDelta > 180) angleDelta = 360 - angleDelta;
        return angleDelta;
    }

    public static double AngleDelta(double a, double b) => (b - a).NormalizeDeg();

    public static double AngleLimit(double width, double tenacity) => (1d - tenacity) * 180d / (width * Math.PI);

    public static double ToRad(this double val) => (Math.PI / 180) * val;

    public static double ToDeg(this double val) => (180 / Math.PI) * val;

    public static double Abs(this double val) => val < 0 ? -val : val;

    public static double Lerp(this double t, double a, double b) => a + (b - a) * t;

    public static double LerpClamped(this double t, double a, double b) => a + (b - a) * t.InRange01();

    public static double ScaleAround(this double v, double m, double s) => (v - m) * s + m;

    public static double WithMin(this double value, double min) => value < min ? min : value;

    public static double WithMax(this double value, double max) => value > max ? max : value;

    public static double InRange01(this double value) => value.InRange(0, 1);

    public static double InRange(this double value, double min, double max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }

    public static int WithMin(this int value, int min) => value < min ? min : value;

    public static int WithMax(this int value, int max) => value > max ? max : value;

    public static int InRange01(this int value) => value.InRange(0, 1);

    public static int InRange(this int value, int min, int max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }

    public static Vector2Int ToInt(this Vector2 vec) => new Vector2Int((int) vec.x, (int) vec.y);

    public static bool NullOrEmpty<T>(this IEnumerable<T> enumerable) => enumerable == null || !enumerable.Any();

    public static T ValueAt<T>(this IGridFunction<T> func, Vector2d pos) => func.ValueAt(pos.x, pos.z);

    public static bool ElementsEqual<T>(this IReadOnlyCollection<T> a, IReadOnlyCollection<T> b) => a.Count == b.Count || a.All(b.Contains);

    public static bool AddUnique<T>(this ICollection<T> list, T item)
    {
        if (list.Contains(item)) return false;
        list.Add(item);
        return true;
    }

    public static double LargestDivisorLessThanOrEqual(double value, double limit) => value / Math.Ceiling(value / limit);

    public static bool BalancedTraversal(ref int a, ref int b, ref int ptr, int limitA, int limitB)
    {
        if (a == ptr.WithMax(limitA) && b == ptr.WithMax(limitB))
        {
            ptr++;

            if (ptr <= limitA)
            {
                a = ptr;
                b = 0;
            }
            else if (ptr <= limitB)
            {
                b = ptr;
                a = 0;
            }
            else
            {
                return false;
            }
        }
        else if (a == ptr)
        {
            if (ptr <= limitB)
            {
                a = b;
                b = ptr;
            }
            else
            {
                b += 1;
            }
        }
        else if (b == ptr)
        {
            if (ptr <= limitA)
            {
                b = a + 1;
                a = ptr;
            }
            else
            {
                a += 1;
            }
        }
        else
        {
            return false;
        }

        return true;
    }
}
