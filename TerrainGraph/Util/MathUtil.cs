using System;
using System.Collections.Generic;
using System.Linq;

namespace TerrainGraph.Util;

public static class MathUtil
{
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

    public static bool NullOrEmpty<T>(this IEnumerable<T> enumerable) => enumerable == null || !enumerable.Any();

    public static T ValueAt<T>(this IGridFunction<T> func, Vector2d pos) => func.ValueAt(pos.x, pos.z);

    public static bool ElementsEqual<T>(this IReadOnlyCollection<T> a, IReadOnlyCollection<T> b) => a.Count == b.Count || a.All(b.Contains);

    public static bool AddUnique<T>(this ICollection<T> list, T item)
    {
        if (list.Contains(item)) return false;
        list.Add(item);
        return true;
    }
}
