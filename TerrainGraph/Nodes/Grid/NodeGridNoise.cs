using System;
using NodeEditorFramework;
using NodeEditorFramework.Utilities;
using TerrainGraph.Util;
using UnityEngine;

namespace TerrainGraph;

[Serializable]
public abstract class NodeGridNoise : NodeBase
{
    protected abstract GridFunction.NoiseFunction NoiseFunction { get; }

    [ValueConnectionKnob("Frequency", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob FrequencyKnob;

    [ValueConnectionKnob("Lacunarity", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob LacunarityKnob;

    [ValueConnectionKnob("Persistence", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob PersistenceKnob;

    [ValueConnectionKnob("Scale", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob ScaleKnob;

    [ValueConnectionKnob("Bias", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob BiasKnob;

    [ValueConnectionKnob("Output", Direction.Out, GridFunctionConnection.Id)]
    public ValueConnectionKnob OutputKnob;

    protected virtual double TransformScale => 1;

    public double Frequency = 0.021;
    public double Lacunarity = 2;
    public double Persistence = 0.5;
    public double Scale = 0.5;
    public double Bias = 0.5;

    public int Octaves = 6;
    public bool DynamicSeed;

    public override void NodeGUI()
    {
        OutputKnob.SetPosition(FirstKnobPosition);

        GUILayout.BeginVertical(BoxStyle);

        KnobValueField(FrequencyKnob, ref Frequency);
        KnobValueField(LacunarityKnob, ref Lacunarity);
        KnobValueField(PersistenceKnob, ref Persistence);
        KnobValueField(ScaleKnob, ref Scale);
        KnobValueField(BiasKnob, ref Bias);
        IntField("Octaves", ref Octaves);

        GUILayout.EndVertical();

        if (GUI.changed)
            canvas.OnNodeChange(this);
    }

    public override void FillNodeActionsMenu(NodeEditorInputInfo inputInfo, GenericMenu menu)
    {
        base.FillNodeActionsMenu(inputInfo, menu);
        menu.AddSeparator("");

        if (!DynamicSeed)
        {
            menu.AddItem(new GUIContent("Enable dynamic seed"), false, () =>
            {
                DynamicSeed = true;
                canvas.OnNodeChange(this);
            });
        }
        else
        {
            menu.AddItem(new GUIContent("Disable dynamic seed"), false, () =>
            {
                DynamicSeed = false;
                canvas.OnNodeChange(this);
            });
        }
    }

    public override void RefreshPreview()
    {
        var freq = GetIfConnected<double>(FrequencyKnob);
        var lac = GetIfConnected<double>(LacunarityKnob);
        var pers = GetIfConnected<double>(PersistenceKnob);
        var scale = GetIfConnected<double>(ScaleKnob);
        var bias = GetIfConnected<double>(BiasKnob);

        foreach (var supplier in new[] { freq, lac, pers, scale, bias }) supplier?.ResetState();

        if (freq != null) Frequency = freq.Get();
        if (lac != null) Lacunarity = lac.Get();
        if (pers != null) Persistence = pers.Get();
        if (scale != null) Scale = scale.Get();
        if (bias != null) Bias = bias.Get();
    }

    public override bool Calculate()
    {
        OutputKnob.SetValue<ISupplier<IGridFunction<double>>>(new Output(
            SupplierOrValueFixed(FrequencyKnob, Frequency),
            SupplierOrValueFixed(LacunarityKnob, Lacunarity),
            SupplierOrValueFixed(PersistenceKnob, Persistence),
            SupplierOrValueFixed(ScaleKnob, Scale),
            SupplierOrValueFixed(BiasKnob, Bias),
            Octaves, NoiseFunction, CombinedSeed, TransformScale, DynamicSeed
        ));
        return true;
    }

    private class Output : ISupplier<IGridFunction<double>>
    {
        private readonly GridFunction.NoiseFunction _noiseFunction;
        private readonly ISupplier<double> _frequency;
        private readonly ISupplier<double> _lacunarity;
        private readonly ISupplier<double> _persistence;
        private readonly ISupplier<double> _scale;
        private readonly ISupplier<double> _bias;
        private readonly int _octaves;
        private readonly int _seed;
        private readonly double _transformScale;
        private readonly bool _dynamicSeed;
        private readonly FastRandom _random;

        public Output(
            ISupplier<double> frequency, ISupplier<double> lacunarity,
            ISupplier<double> persistence, ISupplier<double> scale, ISupplier<double> bias, int octaves, GridFunction.NoiseFunction noiseFunction, int seed, double transformScale, bool dynamicSeed)
        {
            _noiseFunction = noiseFunction;
            _frequency = frequency;
            _lacunarity = lacunarity;
            _persistence = persistence;
            _scale = scale;
            _bias = bias;
            _octaves = octaves;
            _seed = seed;
            _transformScale = transformScale;
            _dynamicSeed = dynamicSeed;
            _random = new FastRandom(seed);
        }

        public IGridFunction<double> Get()
        {
            if (!_dynamicSeed) _random.Reinitialise(_seed);
            return new GridFunction.Transform<double>(
                new GridFunction.ScaleWithBias(
                    new GridFunction.NoiseGenerator(
                        _noiseFunction, _frequency.Get(), _lacunarity.Get(), _persistence.Get(), _octaves, _random.Next()
                    ),
                    _scale.Get(), _bias.Get()), _transformScale
            );
        }

        public void ResetState()
        {
            _random.Reinitialise(_seed);
            _frequency.ResetState();
            _lacunarity.ResetState();
            _persistence.ResetState();
            _scale.ResetState();
            _bias.ResetState();
        }
    }
}
