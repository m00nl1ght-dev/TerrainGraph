using System;
using System.Collections.Generic;

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

    public class Cached<T> : ISupplier<T>
    {
        private readonly ISupplier<T> _generator;
        private readonly List<T> _cache;

        private int _iteration;

        public Cached(ISupplier<T> generator, List<T> cache)
        {
            _generator = generator;
            _cache = cache;
        }

        public T Get()
        {
            while (_cache.Count <= _iteration)
            {
                _cache.Add(_generator.Get());
            }

            return _cache[_iteration++];
        }

        public void ResetState()
        {
            _iteration = 0;
        }
    }

    public class CompoundCached<TS,T> : ISupplier<T>
    {
        private readonly ISupplier<TS> _generator;
        private readonly Func<TS,T> _selector;
        private readonly List<TS> _cache;

        private int _iteration;

        public CompoundCached(ISupplier<TS> generator, Func<TS,T> selector, List<TS> cache)
        {
            _generator = generator;
            _selector = selector;
            _cache = cache;
        }

        public T Get()
        {
            while (_cache.Count <= _iteration)
            {
                _cache.Add(_generator.Get());
            }

            return _selector(_cache[_iteration++]);
        }

        public void ResetState()
        {
            _iteration = 0;
        }
    }

    public static T ResetAndGet<T>(this ISupplier<T> supplier)
    {
        supplier.ResetState();
        return supplier.Get();
    }
}
