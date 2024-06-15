using System;
using System.Collections.Generic;
using System.Linq;
using TerrainGraph.Util;
using static TerrainGraph.Util.MathUtil;

#pragma warning disable CS0659

namespace TerrainGraph;

public static class CurveFunction
{
    public static readonly ICurveFunction<double> Zero = new Const<double>(0f);
    public static readonly ICurveFunction<double> One = new Const<double>(1f);

    public static ICurveFunction<T> Of<T>(T value) => new Const<T>(value);

    public class Const<T> : ICurveFunction<T>
    {
        public readonly T Value;

        public Const(T value)
        {
            Value = value;
        }

        public T ValueAt(double x)
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

    public class Select<T> : ICurveFunction<T>
    {
        public readonly ICurveFunction<double> Input;
        public readonly List<ICurveFunction<T>> Options;
        public readonly List<double> Thresholds;

        public Select(
            ICurveFunction<double> input,
            List<ICurveFunction<T>> options,
            List<double> thresholds)
        {
            Input = input;
            Options = options;
            Thresholds = thresholds;
        }

        public T ValueAt(double x)
        {
            var value = Input.ValueAt(x);

            for (int i = 0; i < Math.Min(Thresholds.Count, Options.Count - 1); i++)
            {
                if (value < Thresholds[i]) return Options[i].ValueAt(x);
            }

            return Options[Options.Count - 1].ValueAt(x);
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
            $"{nameof(Options)}: {Options}, " +
            $"{nameof(Thresholds)}: {Thresholds}, " +
            "}";
    }

    public class Interpolate<T> : ICurveFunction<T>
    {
        public readonly ICurveFunction<double> Input;
        public readonly List<ICurveFunction<T>> Options;
        public readonly List<double> Thresholds;
        public readonly Interpolation<T> Interpolation;

        public Interpolate(
            ICurveFunction<double> input,
            List<ICurveFunction<T>> options,
            List<double> thresholds,
            Interpolation<T> interpolation)
        {
            Input = input;
            Options = options;
            Thresholds = thresholds;
            Interpolation = interpolation;
        }

        public T ValueAt(double x)
        {
            var value = Input.ValueAt(x);

            for (int i = 0; i < Math.Min(Thresholds.Count, Options.Count); i++)
            {
                if (value < Thresholds[i])
                {
                    var c = Options[i].ValueAt(x);
                    if (i == 0) return c;
                    var t = (value - Thresholds[i - 1]) / (Thresholds[i] - Thresholds[i - 1]);
                    var p = Options[i - 1].ValueAt(x);
                    return Interpolation(t, p, c);
                }
            }

            return Options[Options.Count - 1].ValueAt(x);
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
            $"{nameof(Options)}: {Options}, " +
            $"{nameof(Thresholds)}: {Thresholds}, " +
            "}";
    }

    public abstract class Dyadic<T> : ICurveFunction<T>
    {
        public readonly ICurveFunction<T> A, B;

        protected Dyadic(ICurveFunction<T> a, ICurveFunction<T> b)
        {
            A = a;
            B = b;
        }

        protected bool Equals(Dyadic<T> other) =>
            A.Equals(other.A) &&
            B.Equals(other.B);

        public abstract T ValueAt(double x);

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
        public Add(ICurveFunction<double> a, ICurveFunction<double> b) : base(a, b) {}

        public override double ValueAt(double x) => A.ValueAt(x) + B.ValueAt(x);

        public override string ToString() => $"{A:F2} + {B:F2}";
    }

    public class Subtract : Dyadic<double>
    {
        public Subtract(ICurveFunction<double> a, ICurveFunction<double> b) : base(a, b) {}

        public override double ValueAt(double x) => A.ValueAt(x) - B.ValueAt(x);

        public override string ToString() => $"{A:F2} - {B:F2}";
    }

    public class Multiply : Dyadic<double>
    {
        public Multiply(ICurveFunction<double> a, ICurveFunction<double> b) : base(a, b) {}

        public override double ValueAt(double x) => A.ValueAt(x) * B.ValueAt(x);

        public override string ToString() => $"{A:F2} * {B:F2}";
    }

    public class Divide : Dyadic<double>
    {
        public Divide(ICurveFunction<double> a, ICurveFunction<double> b) : base(a, b) {}

        public override double ValueAt(double x) => A.ValueAt(x) / B.ValueAt(x);

        public override string ToString() => $"{A:F2} / {B:F2}";
    }

    public class Lerp : ICurveFunction<double>
    {
        public readonly ICurveFunction<double> A, B, T;

        public Lerp(ICurveFunction<double> a, ICurveFunction<double> b, ICurveFunction<double> t)
        {
            A = a;
            B = b;
            T = t;
        }

        public double ValueAt(double x)
        {
            var a = A.ValueAt(x);
            var b = B.ValueAt(x);
            var t = T.ValueAt(x);

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

        public static ICurveFunction<double> Of(ICurveFunction<double> a, ICurveFunction<double> b, double t)
        {
            if (a == null || t >= 1) return b;
            if (b == null || t <= 0) return a;
            if (a.Equals(b)) return a;
            return new Lerp(a, b, new Const<double>(t));
        }

        public override string ToString() => $"LERP {{ {A:F2} to {B:F2} by {T:F2} }}";
    }

    public class ScaleWithBias : ICurveFunction<double>
    {
        public readonly ICurveFunction<double> Input;
        public readonly double Scale;
        public readonly double Bias;

        public ScaleWithBias(ICurveFunction<double> input, double scale, double bias)
        {
            Input = input;
            Scale = scale;
            Bias = bias;
        }

        public double ValueAt(double x)
        {
            return Input.ValueAt(x) * Scale + Bias;
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

    public class ScaleAround : ICurveFunction<double>
    {
        public readonly ICurveFunction<double> V, M, S;

        public ScaleAround(ICurveFunction<double> v, ICurveFunction<double> m, ICurveFunction<double> s)
        {
            V = v;
            M = m;
            S = s;
        }

        public double ValueAt(double x)
        {
            return V.ValueAt(x).ScaleAround(M.ValueAt(x), S.ValueAt(x));
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

    public class Min : ICurveFunction<double>
    {
        public readonly ICurveFunction<double> A, B;
        public readonly double Smoothness;

        public Min(ICurveFunction<double> a, ICurveFunction<double> b, double smoothness = 0)
        {
            A = a;
            B = b;
            Smoothness = smoothness;
        }

        public double ValueAt(double x) => Calculate(A.ValueAt(x), B.ValueAt(x), Smoothness);

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

    public class Max : ICurveFunction<double>
    {
        public readonly ICurveFunction<double> A, B;
        public readonly double Smoothness;

        public Max(ICurveFunction<double> a, ICurveFunction<double> b, double smoothness = 0)
        {
            A = a;
            B = b;
            Smoothness = smoothness;
        }

        public double ValueAt(double x) => Calculate(A.ValueAt(x), B.ValueAt(x), Smoothness);

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

    public class Invert : ICurveFunction<double>
    {
        public readonly ICurveFunction<double> Input, Pivot;
        public readonly bool ApplyBelow, ApplyAbove;

        public Invert(ICurveFunction<double> input, ICurveFunction<double> pivot, bool applyBelow, bool applyAbove)
        {
            Input = input;
            Pivot = pivot;
            ApplyBelow = applyBelow;
            ApplyAbove = applyAbove;
        }

        public double ValueAt(double x)
        {
            var input = Input.ValueAt(x);
            var pivot = Pivot.ValueAt(x);
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

    public class Clamp : ICurveFunction<double>
    {
        public readonly ICurveFunction<double> Input;
        public readonly double ClampMin;
        public readonly double ClampMax;

        public Clamp(ICurveFunction<double> input, double clampMin, double clampMax)
        {
            Input = input;
            ClampMin = clampMin;
            ClampMax = clampMax;
        }

        public double ValueAt(double x)
        {
            return Math.Max(ClampMin, Math.Min(ClampMax, Input.ValueAt(x)));
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

    public class SpanFunction : ICurveFunction<double>
    {
        public readonly double Bias;
        public readonly double Slope;

        public SpanFunction(double bias, double slope)
        {
            Bias = bias;
            Slope = slope;
        }

        public double ValueAt(double x)
        {
            return Bias + Slope * x;
        }

        protected bool Equals(SpanFunction other) =>
            Bias.Equals(other.Bias) &&
            Slope.Equals(other.Slope);

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
            $"{nameof(Slope)}: {Slope:F2}" +
            " }";
    }

    public class Transform<T> : ICurveFunction<T>
    {
        public readonly ICurveFunction<T> Input;
        public readonly double Translate;
        public readonly double Scale;

        public Transform(ICurveFunction<T> input, double translate, double scale = 1)
        {
            Input = input;
            Translate = translate;
            Scale = scale;
        }

        public T ValueAt(double x)
        {
            return Input.ValueAt(x * Scale - Translate);
        }

        protected bool Equals(Transform<T> other) =>
            Input.Equals(other.Input) &&
            Translate.Equals(other.Translate) &&
            Scale.Equals(other.Scale);

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((Transform<T>) obj);
        }

        public override string ToString() => $"TRANSFORM {{ {Input} by {Translate:F2} scaled {Scale:F2} }}";
    }
}
