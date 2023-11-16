using System;
using System.Collections.Generic;
using System.Linq;

namespace TerrainGraph.Util;

public static class MathExtensions
{
    public static double NormalizeRad(this double value)
    {
        var mod = value % 1;
        return mod < 0 ? mod + 1 : mod;
    }
    
    public static double NormalizeDeg(this double value)
    {
        var mod = value % 360;
        return mod < 0 ? mod + 360 : mod;
    }
    
    public static double ToRad(this double val)
    {
        return (Math.PI / 180) * val;
    }
    
    public static double ToDeg(this double val)
    {
        return (180 / Math.PI) * val;
    }
    
    public static double WithMin(this double value, double min) => value < min ? min : value;
    
    public static double WithMax(this double value, double max) => value > max ? max : value;
    
    public static double InRange01(this double value) => value.InRange(0, 1);
    
    public static double InRange(this double value, double min, double max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }

    public static bool NullOrEmpty<T>(this IEnumerable<T> enumerable) => enumerable == null || !enumerable.Any();
}
