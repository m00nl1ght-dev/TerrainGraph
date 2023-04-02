using System;

namespace TerrainGraph;

public static class Supplier
{
    public static Const<double> Zero = Of<double>(0f);
    public static Const<double> One = Of<double>(1f);

    public static Const<T> Of<T>(T value) => new(value);
    public static FromFunc<T> From<T>(Func<T> func, Action reset = null) => new(func, reset);

    public class Const<T> : ISupplier<T>
    {
        public readonly T Value;

        public Const(T value) => Value = value;

        public T Get() => Value;

        public void ResetState() { }
    }

    public class FromFunc<T> : ISupplier<T>
    {
        public readonly Func<T> Func;
        public readonly Action Reset;

        public FromFunc(Func<T> func, Action reset = null)
        {
            Func = func;
            Reset = reset;
        }

        public T Get() => Func();

        public void ResetState()
        {
            Reset?.Invoke();
        }
    }

    public static T ResetAndGet<T>(this ISupplier<T> supplier)
    {
        supplier.ResetState();
        return supplier.Get();
    }
}
