using System;

namespace TerrainGraph;

[Serializable]
public class ConfigurableOverride
{
    public string ConfigId;
    public string KnobName;

    public double MinValue;
    public double MaxValue;

    [NonSerialized]
    public bool OverrideEnabled;

    [NonSerialized]
    public object OverrideValue;

    [NonSerialized]
    public object LastNaturalValue;

    public ISupplier<T> WrapSupplier<T>(ISupplier<T> original)
    {
        if (original != null) return new SupplierWrapper<T>(this, original);
        return OverrideEnabled ? Supplier.Of((T) OverrideValue) : null;
    }

    private class SupplierWrapper<T> : ISupplier<T>
    {
        private readonly ConfigurableOverride Config;
        private readonly ISupplier<T> Inner;

        public SupplierWrapper(ConfigurableOverride config, ISupplier<T> inner)
        {
            Config = config;
            Inner = inner;
        }

        public T Get()
        {
            var value = Inner.Get();

            Config.LastNaturalValue = value;

            if (Config.OverrideEnabled)
                return (T) Config.OverrideValue;

            return value;
        }

        public void ResetState()
        {
            Inner.ResetState();
        }
    }
}
