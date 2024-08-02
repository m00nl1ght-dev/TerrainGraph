using System;
using NodeEditorFramework;
using TerrainGraph.Util;
using UnityEngine;
using static TerrainGraph.GridFunction;

namespace TerrainGraph;

[Serializable]
[Node(false, "Grid/Turbulence", 215)]
public class NodeGridTurbulence : NodeBase
{
    public const string ID = "gridTurbulence";
    public override string GetID => ID;

    public override string Title => "Turbulence";

    [ValueConnectionKnob("Intensity X", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob IntensityXKnob;

    [ValueConnectionKnob("Intensity Z", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob IntensityZKnob;

    [ValueConnectionKnob("Output X", Direction.Out, GridFunctionConnection.Id)]
    public ValueConnectionKnob OutputXKnob;

    [ValueConnectionKnob("Output Z", Direction.Out, GridFunctionConnection.Id)]
    public ValueConnectionKnob OutputZKnob;

    public double IntensityX = 1;
    public double IntensityZ = 1;

    public override void NodeGUI()
    {
        GUILayout.BeginVertical(BoxStyle);

        KnobValueField(IntensityXKnob, ref IntensityX);
        OutputXKnob.SetPosition();

        KnobValueField(IntensityZKnob, ref IntensityZ);
        OutputZKnob.SetPosition();

        GUILayout.EndVertical();

        if (GUI.changed)
            canvas.OnNodeChange(this);
    }

    public override void RefreshPreview()
    {
        var intensityX = GetIfConnected<double>(IntensityXKnob);
        var intensityZ = GetIfConnected<double>(IntensityZKnob);

        intensityX?.ResetState();
        intensityZ?.ResetState();

        if (intensityX != null) IntensityX = intensityX.Get();
        if (intensityZ != null) IntensityZ = intensityZ.Get();
    }

    public override void CleanUpGUI()
    {
        if (IntensityXKnob.connected()) IntensityX = 0;
        if (IntensityZKnob.connected()) IntensityZ = 0;
    }

    public override bool Calculate()
    {
        OutputXKnob.SetValue<ISupplier<IGridFunction<double>>>(new Output(
            SupplierOrFallback(IntensityXKnob, IntensityX),
            CombinedSeed, TerrainCanvas.CreateRandomInstance(),
            true, TerrainCanvas.GridFullSize
        ));
        OutputZKnob.SetValue<ISupplier<IGridFunction<double>>>(new Output(
            SupplierOrFallback(IntensityZKnob, IntensityZ),
            CombinedSeed, TerrainCanvas.CreateRandomInstance(),
            false, TerrainCanvas.GridFullSize
        ));
        return true;
    }

    public class Output : ISupplier<IGridFunction<double>>
    {
        private readonly ISupplier<double> _intensity;
        private readonly int _seed;
        private readonly IRandom _random;
        private readonly bool _cosine;
        private readonly int _gridSize;

        public Output(ISupplier<double> intensity, int seed, IRandom random, bool cosine, int gridSize)
        {
            _intensity = intensity;
            _seed = seed;
            _random = random;
            _cosine = cosine;
            _gridSize = gridSize;
        }

        public IGridFunction<double> Get()
        {
            var intensity = _intensity.Get();
            var grid = new double[_gridSize, _gridSize];

            for (int x = 0; x < _gridSize; x++)
            {
                for (int z = 0; z < _gridSize; z++)
                {
                    var angle = _random.NextDouble() * 2 * Math.PI;
                    var length = Math.Sqrt(_random.NextDouble());
                    grid[x, z] = (_cosine ? Math.Cos(angle) : Math.Sin(angle)) * length * intensity;
                }
            }

            return new BilinearDiscrete(new WrapCoords(new Cache<double>(grid), new Vector2d(_gridSize, _gridSize)));
        }

        public void ResetState()
        {
            _intensity.ResetState();
            _random.Reinitialise(_seed);
        }
    }
}
