using System;
using System.Collections.Generic;
using System.Linq;
using TerrainGraph.Util;
using static TerrainGraph.Util.MathUtil;

#pragma warning disable CS0659

namespace TerrainGraph;

public static class GridFunction
{
    public static readonly IGridFunction<double> Zero = new Const<double>(0f);
    public static readonly IGridFunction<double> One = new Const<double>(1f);

    public static IGridFunction<T> Of<T>(T value) => new Const<T>(value);

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

        public override string ToString() => $"{Value:F2}";
    }

    public class Select<T> : IGridFunction<T>
    {
        public readonly IGridFunction<double> Input;
        public readonly List<IGridFunction<T>> Options;
        public readonly List<double> Thresholds;

        public Select(
            IGridFunction<double> input,
            List<IGridFunction<T>> options,
            List<double> thresholds)
        {
            Input = input;
            Options = options;
            Thresholds = thresholds;
        }

        public T ValueAt(double x, double z)
        {
            var value = Input.ValueAt(x, z);

            for (int i = 0; i < Math.Min(Thresholds.Count, Options.Count - 1); i++)
            {
                if (value < Thresholds[i]) return Options[i].ValueAt(x, z);
            }

            return Options[Options.Count - 1].ValueAt(x, z);
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

        public override string ToString() =>
            "SELECT { " +
            $"{nameof(Input)}: {Input}, " +
            $"{nameof(Options)}: {string.Join(", ", Options.Select(v => $"{v:F2}"))}, " +
            $"{nameof(Thresholds)}: {string.Join(", ", Thresholds.Select(v => $"{v:F2}"))}, " +
            "}";
    }

    public class Interpolate<T> : IGridFunction<T>
    {
        public readonly IGridFunction<double> Input;
        public readonly List<IGridFunction<T>> Options;
        public readonly List<double> Thresholds;
        public readonly Interpolation<T> Interpolation;

        public Interpolate(
            IGridFunction<double> input,
            List<IGridFunction<T>> options,
            List<double> thresholds,
            Interpolation<T> interpolation)
        {
            Input = input;
            Options = options;
            Thresholds = thresholds;
            Interpolation = interpolation;
        }

        public T ValueAt(double x, double z)
        {
            var value = Input.ValueAt(x, z);

            for (int i = 0; i < Math.Min(Thresholds.Count, Options.Count); i++)
            {
                if (value < Thresholds[i])
                {
                    var c = Options[i].ValueAt(x, z);
                    if (i == 0) return c;
                    var t = (value - Thresholds[i - 1]) / (Thresholds[i] - Thresholds[i - 1]);
                    var p = Options[i - 1].ValueAt(x, z);
                    return Interpolation(t, p, c);
                }
            }

            return Options[Options.Count - 1].ValueAt(x, z);
        }

        protected bool Equals(Interpolate<T> other) =>
            Input.Equals(other.Input) &&
            Options.SequenceEqual(other.Options) &&
            Thresholds.SequenceEqual(other.Thresholds);

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((Interpolate<T>) obj);
        }

        public override string ToString() =>
            "INTERPOLATE { " +
            $"{nameof(Input)}: {Input}, " +
            $"{nameof(Options)}: {string.Join(", ", Options.Select(v => $"{v:F2}"))}, " +
            $"{nameof(Thresholds)}: {string.Join(", ", Thresholds.Select(v => $"{v:F2}"))}, " +
            "}";
    }

    public abstract class Dyadic<T> : IGridFunction<T>
    {
        public readonly IGridFunction<T> A, B;

        protected Dyadic(IGridFunction<T> a, IGridFunction<T> b)
        {
            A = a;
            B = b;
        }

        protected bool Equals(Dyadic<T> other) =>
            A.Equals(other.A) &&
            B.Equals(other.B);

        public abstract T ValueAt(double x, double z);

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((Dyadic<T>) obj);
        }
    }

    public class Add : Dyadic<double>
    {
        public Add(IGridFunction<double> a, IGridFunction<double> b) : base(a, b) {}

        public override double ValueAt(double x, double z) => A.ValueAt(x, z) + B.ValueAt(x, z);

        public override string ToString() => $"{A:F2} + {B:F2}";
    }

    public class Subtract : Dyadic<double>
    {
        public Subtract(IGridFunction<double> a, IGridFunction<double> b) : base(a, b) {}

        public override double ValueAt(double x, double z) => A.ValueAt(x, z) - B.ValueAt(x, z);

        public override string ToString() => $"{A:F2} - {B:F2}";
    }

    public class Multiply : Dyadic<double>
    {
        public Multiply(IGridFunction<double> a, IGridFunction<double> b) : base(a, b) {}

        public override double ValueAt(double x, double z) => A.ValueAt(x, z) * B.ValueAt(x, z);

        public override string ToString() => $"{A:F2} * {B:F2}";
    }

    public class Divide : Dyadic<double>
    {
        public Divide(IGridFunction<double> a, IGridFunction<double> b) : base(a, b) {}

        public override double ValueAt(double x, double z) => A.ValueAt(x, z) / B.ValueAt(x, z);

        public override string ToString() => $"{A:F2} / {B:F2}";
    }

    public class Lerp : IGridFunction<double>
    {
        public readonly IGridFunction<double> A, B, T;

        public Lerp(IGridFunction<double> a, IGridFunction<double> b, IGridFunction<double> t)
        {
            A = a;
            B = b;
            T = t;
        }

        public double ValueAt(double x, double z)
        {
            var a = A.ValueAt(x, z);
            var b = B.ValueAt(x, z);
            var t = T.ValueAt(x, z);

            return a + (b - a) * t;
        }

        protected bool Equals(Lerp other) =>
            A.Equals(other.A) &&
            B.Equals(other.B) &&
            T.Equals(other.T);

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((Lerp) obj);
        }

        public static IGridFunction<double> Of(IGridFunction<double> a, IGridFunction<double> b, double t)
        {
            if (a == null || t >= 1) return b;
            if (b == null || t <= 0) return a;
            if (a.Equals(b)) return a;
            return new Lerp(a, b, new Const<double>(t));
        }

        public override string ToString() => $"LERP {{ {A:F2} to {B:F2} by {T:F2} }}";
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

        public override string ToString() => $"{Input} * {Scale:F2} + {Bias:F2}";
    }

    public class ScaleAround : IGridFunction<double>
    {
        public readonly IGridFunction<double> V, M, S;

        public ScaleAround(IGridFunction<double> v, IGridFunction<double> m, IGridFunction<double> s)
        {
            V = v;
            M = m;
            S = s;
        }

        public double ValueAt(double x, double z)
        {
            return V.ValueAt(x, z).ScaleAround(M.ValueAt(x, z), S.ValueAt(x, z));
        }

        protected bool Equals(ScaleAround other) =>
            V.Equals(other.V) &&
            M.Equals(other.M) &&
            S.Equals(other.S);

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((ScaleAround) obj);
        }

        public override string ToString() => $"SCALE {{ {V} around {M} by {S} }}";
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

        public override string ToString() => Smoothness > 0f
            ? $"MIN {{ {A:F2} and {B:F2} smooth {Smoothness:F2} }}"
            : $"MIN {{ {A:F2} and {B:F2} }}";
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

        public override string ToString() => Smoothness > 0f
            ? $"MAX {{ {A:F2} and {B:F2} smooth {Smoothness:F2} }}"
            : $"MAX {{ {A:F2} and {B:F2} }}";
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

        public override string ToString() => $"INVERT {{ {Input} at {Pivot} below {ApplyBelow} above {ApplyAbove} }}";
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

        public override string ToString() =>
            ClampMin == double.MinValue && ClampMax == double.MaxValue ? $"CLAMP {{ {Input} }}" :
            ClampMin == double.MinValue ? $"CLAMP {{ {Input} below {ClampMax:F2} }}" :
            ClampMax == double.MaxValue ? $"CLAMP {{ {Input} above {ClampMin:F2} }}" :
            $"CLAMP {{ {Input} between {ClampMin:F2} and {ClampMax:F2} }}";
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

        public override string ToString() =>
            "SPAN { " +
            $"{nameof(Bias)}: {Bias:F2}, " +
            $"{nameof(OriginX)}: {OriginX:F2}, " +
            $"{nameof(OriginZ)}: {OriginZ:F2}, " +
            $"{nameof(SpanPx)}: {SpanPx:F2}, " +
            $"{nameof(SpanNx)}: {SpanNx:F2}, " +
            $"{nameof(SpanPz)}: {SpanPz:F2}, " +
            $"{nameof(SpanNz)}: {SpanNz:F2}, " +
            $"{nameof(Circular)}: {Circular}" +
            " }";
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

        public override string ToString() => $"ROTATE {{ {Input} around {PivotX:F2} | {PivotZ:F2} by {Angle:F2} }}";
    }

    public class ApplyCurve<T> : IGridFunction<T>
    {
        public readonly IGridFunction<double> Input;
        public readonly ICurveFunction<T> Curve;

        public ApplyCurve(IGridFunction<double> input, ICurveFunction<T> curve)
        {
            Input = input;
            Curve = curve;
        }

        public T ValueAt(double x, double z) => Curve.ValueAt(Input.ValueAt(x, z));

        protected bool Equals(ApplyCurve<T> other) =>
            Input.Equals(other.Input) &&
            Curve.Equals(other.Curve);

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((ApplyCurve<T>) obj);
        }

        public override string ToString() => $"APPLY CURVE {{ {Curve} on {Input} }}";
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

        public override string ToString() => $"DELTA MAP {{ {Input} with step {Step:F2} }}";
    }

    public class KernelAggregation<T> : IGridFunction<T>
    {
        public readonly IGridFunction<T> Input;
        public readonly Func<T, T, T> Aggregation;
        public readonly GridKernel Kernel;

        public KernelAggregation(IGridFunction<T> input, Func<T, T, T> aggregation, GridKernel kernel)
        {
            Input = input;
            Aggregation = aggregation;
            Kernel = kernel;
        }

        public T ValueAt(double x, double z)
        {
            return Kernel.CalculateAt(Input, new Vector2d(x, z), Aggregation);
        }

        protected bool Equals(KernelAggregation<T> other) =>
            Equals(Input, other.Input) &&
            Equals(Aggregation, other.Aggregation) &&
            Equals(Kernel, other.Kernel);

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((KernelAggregation<T>) obj);
        }

        public override string ToString() => $"KERNEL {{ {Input} }}";
    }

    public class BilinearDiscrete : IGridFunction<double>
    {
        public readonly IGridFunction<double> Input;

        public BilinearDiscrete(IGridFunction<double> input)
        {
            Input = input;
        }

        public double ValueAt(double x, double z)
        {
            var mx = x % 1;
            var mz = z % 1;
            var fx = Math.Floor(x);
            var fz = Math.Floor(z);

            var p00 = Input.ValueAt(fx, fz);
            if (mx == 0 && mz == 0) return p00;

            var p10 = Input.ValueAt(fx + 1, fz);
            var p01 = Input.ValueAt(fx, fz + 1);
            var p11 = Input.ValueAt(fx + 1, fz + 1);

            var v1 = (1 - mx) * p00 + mx * p10;
            var v2 = (1 - mx) * p01 + mx * p11;

            return (1 - mz) * v1 + mz * v2;
        }

        protected bool Equals(BilinearDiscrete other) =>
            Input.Equals(other.Input);

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((BilinearDiscrete) obj);
        }

        public override string ToString() => $"INTERPOLATE DISCRETE {{ {Input} }}";
    }

    public class WrapCoords : IGridFunction<double>
    {
        public readonly IGridFunction<double> Input;
        public readonly Vector2d Size;

        public WrapCoords(IGridFunction<double> input, Vector2d size)
        {
            Input = input;
            Size = size;
        }

        public double ValueAt(double x, double z)
        {
            return Input.ValueAt(x % Size.x, z % Size.z);
        }

        protected bool Equals(WrapCoords other) =>
            Equals(Input, other.Input) &&
            Size.Equals(other.Size);

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((WrapCoords) obj);
        }

        public override string ToString() => $"WRAP COORDS {{ {Input} at size {Size} }}";
    }

    public class Transform<T> : IGridFunction<T>
    {
        public readonly IGridFunction<T> Input;
        public readonly double TranslateX;
        public readonly double TranslateZ;
        public readonly double ScaleX;
        public readonly double ScaleZ;

        public Transform(IGridFunction<T> input, double translateX, double translateZ, double scaleX = 1, double scaleZ = 1)
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

        public override string ToString() => $"TRANSFORM {{ {Input} by {TranslateX:F2} | {TranslateZ:F2} scaled {ScaleX:F2} | {ScaleZ:F2} }}";
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

        public override string ToString() => $"CACHE {{ size {SizeX} | {SizeZ} with fallback {Fallback} }}";
    }
}
