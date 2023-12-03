using System;
using System.Collections.Generic;
using System.Linq;
using TerrainGraph.Util;

#pragma warning disable CS0659

namespace TerrainGraph;

public static class GridFunction
{
    public static readonly Const<double> Zero = new(0f);
    public static readonly Const<double> One = new(1f);

    public static Const<T> Of<T>(T value) => new(value);

    public class Const<T> : IGridFunction<T>
    {
        public readonly T Value;

        public Const(T value)
        {
            Value = value;
        }

        public T ValueAt(double x, double z)
        {
            return Value;
        }

        protected bool Equals(Const<T> other)
        {
            return EqualityComparer<T>.Default.Equals(Value, other.Value);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((Const<T>) obj);
        }
    }

    public class Select<T> : IGridFunction<T>
    {
        public readonly IGridFunction<double> Input;
        public readonly List<IGridFunction<T>> Options;
        public readonly List<double> Thresholds;
        public readonly Func<T, int, T> PostProcess;

        public Select(IGridFunction<double> input, List<IGridFunction<T>> options, List<double> thresholds, Func<T, int, T> postProcess = null)
        {
            Input = input;
            Options = options;
            Thresholds = thresholds;
            PostProcess = postProcess ?? ((v, _) => v);
        }

        public T ValueAt(double x, double z)
        {
            var value = Input.ValueAt(x, z);
            for (int i = 0; i < Math.Min(Thresholds.Count, Options.Count - 1); i++)
                if (value < Thresholds[i])
                    return PostProcess(Options[i].ValueAt(x, z), i);
            return PostProcess(Options[Options.Count - 1].ValueAt(x, z), Options.Count - 1);
        }

        protected bool Equals(Select<T> other) =>
            Input.Equals(other.Input) &&
            Options.SequenceEqual(other.Options) &&
            Thresholds.SequenceEqual(other.Thresholds);

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((Select<T>) obj);
        }
    }

    public class Add : IGridFunction<double>
    {
        public readonly IGridFunction<double> A, B;

        public Add(IGridFunction<double> a, IGridFunction<double> b)
        {
            A = a;
            B = b;
        }

        public double ValueAt(double x, double z)
        {
            return A.ValueAt(x, z) + B.ValueAt(x, z);
        }

        protected bool Equals(Add other) =>
            A.Equals(other.A) &&
            B.Equals(other.B);

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((Add) obj);
        }
    }

    public class Subtract : IGridFunction<double>
    {
        public readonly IGridFunction<double> A, B;

        public Subtract(IGridFunction<double> a, IGridFunction<double> b)
        {
            A = a;
            B = b;
        }

        public double ValueAt(double x, double z)
        {
            return A.ValueAt(x, z) - B.ValueAt(x, z);
        }

        protected bool Equals(Subtract other) =>
            A.Equals(other.A) &&
            B.Equals(other.B);

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((Subtract) obj);
        }
    }

    public class Multiply : IGridFunction<double>
    {
        public readonly IGridFunction<double> A, B;

        public Multiply(IGridFunction<double> a, IGridFunction<double> b)
        {
            A = a;
            B = b;
        }

        public double ValueAt(double x, double z)
        {
            return A.ValueAt(x, z) * B.ValueAt(x, z);
        }

        protected bool Equals(Multiply other) =>
            A.Equals(other.A) &&
            B.Equals(other.B);

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((Multiply) obj);
        }
    }

    public class Divide : IGridFunction<double>
    {
        public readonly IGridFunction<double> A, B;

        public Divide(IGridFunction<double> a, IGridFunction<double> b)
        {
            A = a;
            B = b;
        }

        public double ValueAt(double x, double z)
        {
            return A.ValueAt(x, z) / B.ValueAt(x, z);
        }

        protected bool Equals(Divide other) =>
            A.Equals(other.A) &&
            B.Equals(other.B);

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((Divide) obj);
        }
    }

    public class ScaleWithBias : IGridFunction<double>
    {
        public readonly IGridFunction<double> Input;
        public readonly double Scale;
        public readonly double Bias;

        public ScaleWithBias(IGridFunction<double> input, double scale, double bias)
        {
            Input = input;
            Scale = scale;
            Bias = bias;
        }

        public double ValueAt(double x, double z)
        {
            return Input.ValueAt(x, z) * Scale + Bias;
        }

        protected bool Equals(ScaleWithBias other) =>
            Input.Equals(other.Input) &&
            Scale.Equals(other.Scale) &&
            Bias.Equals(other.Bias);

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((ScaleWithBias) obj);
        }
    }

    public class Min : IGridFunction<double>
    {
        public readonly IGridFunction<double> A, B;
        public readonly double Smoothness;

        public Min(IGridFunction<double> a, IGridFunction<double> b, double smoothness = 0)
        {
            A = a;
            B = b;
            Smoothness = smoothness;
        }

        public double ValueAt(double x, double z) => Calculate(A.ValueAt(x, z), B.ValueAt(x, z), Smoothness);

        public static double Calculate(double a, double b, double smoothness)
        {
            if (smoothness <= 0f) return Math.Min(a, b);

            double max = Math.Max(a, b) * smoothness;
            double min = Math.Min(a, b) * smoothness;

            return (min - Math.Log(1f + Math.Exp(min - max))) / smoothness;
        }

        protected bool Equals(Min other) =>
            A.Equals(other.A) && B.Equals(other.B) &&
            Smoothness.Equals(other.Smoothness);

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((Min) obj);
        }
    }

    public class Max : IGridFunction<double>
    {
        public readonly IGridFunction<double> A, B;
        public readonly double Smoothness;

        public Max(IGridFunction<double> a, IGridFunction<double> b, double smoothness = 0)
        {
            A = a;
            B = b;
            Smoothness = smoothness;
        }

        public double ValueAt(double x, double z) => Calculate(A.ValueAt(x, z), B.ValueAt(x, z), Smoothness);

        public static double Calculate(double a, double b, double smoothness)
        {
            if (smoothness <= 0f) return Math.Max(a, b);

            double max = Math.Max(a, b) * smoothness;
            double min = Math.Min(a, b) * smoothness;

            return (max + Math.Log(1f + Math.Exp(min - max))) / smoothness;
        }

        protected bool Equals(Max other) =>
            A.Equals(other.A) && B.Equals(other.B) &&
            Smoothness.Equals(other.Smoothness);

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((Max) obj);
        }
    }

    public class Invert : IGridFunction<double>
    {
        public readonly IGridFunction<double> Input, Pivot;
        public readonly bool ApplyBelow, ApplyAbove;

        public Invert(IGridFunction<double> input, IGridFunction<double> pivot, bool applyBelow, bool applyAbove)
        {
            Input = input;
            Pivot = pivot;
            ApplyBelow = applyBelow;
            ApplyAbove = applyAbove;
        }

        public double ValueAt(double x, double z)
        {
            var input = Input.ValueAt(x, z);
            var pivot = Pivot.ValueAt(x, z);
            if (input < pivot ? ApplyBelow : ApplyAbove) return pivot + (pivot - input);
            return input;
        }

        protected bool Equals(Invert other) =>
            Input.Equals(other.Input) &&
            Pivot.Equals(other.Pivot) &&
            ApplyBelow == other.ApplyBelow &&
            ApplyAbove == other.ApplyAbove;

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((Invert) obj);
        }
    }

    public class Clamp : IGridFunction<double>
    {
        public readonly IGridFunction<double> Input;
        public readonly double ClampMin;
        public readonly double ClampMax;

        public Clamp(IGridFunction<double> input, double clampMin, double clampMax)
        {
            Input = input;
            ClampMin = clampMin;
            ClampMax = clampMax;
        }

        public double ValueAt(double x, double z)
        {
            return Math.Max(ClampMin, Math.Min(ClampMax, Input.ValueAt(x, z)));
        }

        protected bool Equals(Clamp other) =>
            Input.Equals(other.Input) &&
            ClampMin.Equals(other.ClampMin) &&
            ClampMax.Equals(other.ClampMax);

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((Clamp) obj);
        }
    }

    public class SpanFunction : IGridFunction<double>
    {
        public readonly double Bias;
        public readonly double OriginX;
        public readonly double OriginZ;
        public readonly double SpanPx;
        public readonly double SpanNx;
        public readonly double SpanPz;
        public readonly double SpanNz;
        public readonly bool Circular;

        public SpanFunction(
            double bias, double originX, double originZ, double spanPx, double spanNx,
            double spanPz, double spanNz, bool circular)
        {
            Bias = bias;
            OriginX = originX;
            OriginZ = originZ;
            SpanPx = spanPx;
            SpanNx = spanNx;
            SpanPz = spanPz;
            SpanNz = spanNz;
            Circular = circular;
        }

        public double ValueAt(double x, double z)
        {
            double diffX = x - OriginX;
            double diffZ = z - OriginZ;

            double spanX = diffX < 0 ? SpanNx : SpanPx;
            double spanZ = diffZ < 0 ? SpanNz : SpanPz;

            double valX = spanX == 0 ? 0 : Math.Abs(diffX) / spanX;
            double valZ = spanZ == 0 ? 0 : Math.Abs(diffZ) / spanZ;

            if (Circular)
            {
                return Bias + Math.Sqrt(Math.Pow(valX, 2) + Math.Pow(valZ, 2)) * (spanX < 0 || spanZ < 0 ? -1 : 1);
            }

            return Bias + valX + valZ;
        }

        protected bool Equals(SpanFunction other) =>
            Bias.Equals(other.Bias) &&
            OriginX.Equals(other.OriginX) &&
            OriginZ.Equals(other.OriginZ) &&
            SpanPx.Equals(other.SpanPx) &&
            SpanNx.Equals(other.SpanNx) &&
            SpanPz.Equals(other.SpanPz) &&
            SpanNz.Equals(other.SpanNz) &&
            Circular == other.Circular;

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((SpanFunction) obj);
        }
    }

    public class Rotate<T> : IGridFunction<T>
    {
        public readonly IGridFunction<T> Input;
        public readonly double PivotX;
        public readonly double PivotZ;
        public readonly double Angle;

        public Rotate(IGridFunction<T> input, double pivotX, double pivotZ, double angle)
        {
            Input = input;
            PivotX = pivotX;
            PivotZ = pivotZ;
            Angle = angle;
        }

        public T ValueAt(double x, double z) => Calculate(Input, x, z, PivotX, PivotZ, Angle);

        public static T Calculate(IGridFunction<T> input, double x, double z, double pivotX, double pivotZ, double angle)
        {
            double radians = angle.ToRad();

            double sin = Math.Sin(radians);
            double cos = Math.Cos(radians);

            x -= pivotX;
            z -= pivotZ;

            double nx = x * cos - z * sin;
            double nz = x * sin + z * cos;

            nx += pivotX;
            nz += pivotZ;

            return input.ValueAt(nx, nz);
        }

        protected bool Equals(Rotate<T> other) =>
            Input.Equals(other.Input) &&
            PivotX.Equals(other.PivotX) &&
            PivotZ.Equals(other.PivotZ) &&
            Angle.Equals(other.Angle);

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((Rotate<T>) obj);
        }
    }

    public class DeltaMap : IGridFunction<double>
    {
        public readonly IGridFunction<double> Input;
        public readonly double Step;

        public DeltaMap(IGridFunction<double> input, double step)
        {
            Input = input;
            Step = step;
        }

        public double ValueAt(double x, double z)
        {
            var mm = Input.ValueAt(x, z);
            var xm = Input.ValueAt(x - Step, z);
            var xp = Input.ValueAt(x + Step, z);
            var zm = Input.ValueAt(x, z - Step);
            var zp = Input.ValueAt(x, z + Step);

            return (Math.Abs(mm - xm) + Math.Abs(mm - xp) + Math.Abs(mm - zm) + Math.Abs(mm - zp)) / 4;
        }

        protected bool Equals(DeltaMap other) =>
            Input.Equals(other.Input) &&
            Step.Equals(other.Step);

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((DeltaMap) obj);
        }
    }

    public class Transform<T> : IGridFunction<T>
    {
        public readonly IGridFunction<T> Input;
        public readonly double TranslateX;
        public readonly double TranslateZ;
        public readonly double ScaleX;
        public readonly double ScaleZ;

        public Transform(IGridFunction<T> input, double translateX, double translateZ, double scaleX, double scaleZ)
        {
            Input = input;
            TranslateX = translateX;
            TranslateZ = translateZ;
            ScaleX = scaleX;
            ScaleZ = scaleZ;
        }

        public Transform(IGridFunction<T> input, double scale)
        {
            Input = input;
            ScaleX = scale;
            ScaleZ = scale;
        }

        public T ValueAt(double x, double z)
        {
            return Input.ValueAt(x * ScaleX - TranslateX, z * ScaleZ - TranslateZ);
        }

        protected bool Equals(Transform<T> other) =>
            Input.Equals(other.Input) &&
            TranslateX.Equals(other.TranslateX) &&
            TranslateZ.Equals(other.TranslateZ) &&
            ScaleX.Equals(other.ScaleX) &&
            ScaleZ.Equals(other.ScaleZ);

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((Transform<T>) obj);
        }
    }

    public class Cache<T> : IGridFunction<T>
    {
        public readonly int SizeX;
        public readonly int SizeZ;
        public readonly T[,] Grid;
        public readonly T Fallback;

        public Cache(T[,] grid, T fallback = default)
        {
            SizeX = grid.GetLength(0);
            SizeZ = grid.GetLength(1);
            Grid = grid;
            Fallback = fallback;
        }

        public Cache(int sizeX, int sizeZ, T fallback = default)
        {
            SizeX = sizeX;
            SizeZ = sizeZ;
            Grid = new T[sizeX, sizeZ];
            Fallback = fallback;
        }

        public T ValueAt(double x, double z)
        {
            var ix = (int) Math.Round(x);
            var iz = (int) Math.Round(z);
            return ix < 0 || ix >= SizeX || iz < 0 || iz >= SizeZ ? Fallback : Grid[ix, iz];
        }

        protected bool Equals(Cache<T> other) =>
            SizeX == other.SizeX &&
            SizeZ == other.SizeZ &&
            Grid.Equals(other.Grid) &&
            EqualityComparer<T>.Default.Equals(Fallback, other.Fallback);

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((Cache<T>) obj);
        }
    }
}
