using System;
using System.Xml.Serialization;

namespace TerrainGraph;

[Serializable]
public class ConfigurableOverride
{
    public string PropertyId;
    public string PropertyLabel;

    public int SortOrder;

    public string KnobName;
    public string KnobType;

    public double MinValue;
    public double MaxValue;

    [XmlIgnore]
    public bool OverrideEnabled;

    [XmlIgnore]
    public object OverrideValue;

    [XmlIgnore]
    public object LastNaturalValue;

    public ISupplier<T> WrapSupplier<T>(ISupplier<T> original)
    {
        if (original != null) return new SupplierWrapper<T>(this, original);
        return OverrideEnabled ? Supplier.Of((T) OverrideValue) : null;
    }

    public void Reset()
    {
        OverrideEnabled = false;
        OverrideValue = null;
        LastNaturalValue = null;
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
