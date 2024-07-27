using System;
using System.Collections.Generic;
using NodeEditorFramework;
using TerrainGraph.Util;
using UnityEngine;

namespace TerrainGraph;

[Serializable]
[Node(false, "Grid/Morph/Group Filter", 230)]
public class NodeGridMorphGroupFilter : NodeBase
{
    public const string ID = "gridMorphGroupFilter";
    public override string GetID => ID;

    public override string Title => "Morph: Group Filter";

    [ValueConnectionKnob("Input", Direction.In, GridFunctionConnection.Id)]
    public ValueConnectionKnob InputKnob;

    [ValueConnectionKnob("Threshold", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob ThresholdKnob;

    [ValueConnectionKnob("Min size", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob MinGroupSizeKnob;

    [ValueConnectionKnob("Max size", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob MaxGroupSizeKnob;

    [ValueConnectionKnob("Output", Direction.Out, GridFunctionConnection.Id)]
    public ValueConnectionKnob OutputKnob;

    public double Threshold = 1;
    public double MinGroupSize;
    public double MaxGroupSize;

    public override void NodeGUI()
    {
        InputKnob.SetPosition(FirstKnobPosition);
        OutputKnob.SetPosition(FirstKnobPosition);

        GUILayout.BeginVertical(BoxStyle);

        GUILayout.BeginHorizontal(BoxStyle);
        GUILayout.Label("Input", BoxLayout);
        GUILayout.EndHorizontal();

        KnobValueField(ThresholdKnob, ref Threshold);
        KnobValueField(MinGroupSizeKnob, ref MinGroupSize);
        KnobValueField(MaxGroupSizeKnob, ref MaxGroupSize);

        GUILayout.EndVertical();

        if (GUI.changed)
            canvas.OnNodeChange(this);
    }

    public override void RefreshPreview()
    {
        var threshold = GetIfConnected<double>(ThresholdKnob);
        var minGroupSize = GetIfConnected<double>(MinGroupSizeKnob);
        var maxGroupSize = GetIfConnected<double>(MaxGroupSizeKnob);

        threshold?.ResetState();
        minGroupSize?.ResetState();
        maxGroupSize?.ResetState();

        if (threshold != null) Threshold = threshold.Get();
        if (minGroupSize != null) MinGroupSize = minGroupSize.Get();
        if (maxGroupSize != null) MaxGroupSize = maxGroupSize.Get();
    }

    public override bool Calculate()
    {
        var cache = new List<IGridFunction<double>>(5);

        var output = new Output(
            SupplierOrFallback(InputKnob, GridFunction.Zero),
            SupplierOrFallback(ThresholdKnob, Threshold),
            SupplierOrFallback(MinGroupSizeKnob, MinGroupSize),
            SupplierOrFallback(MaxGroupSizeKnob, MaxGroupSize),
            0d, TerrainCanvas.GridFullSize
        );

        OutputKnob.SetValue<ISupplier<IGridFunction<double>>>(
            new Supplier.Cached<IGridFunction<double>>(output, cache)
        );

        return true;
    }

    public class Output : ISupplier<IGridFunction<double>>
    {
        private readonly ISupplier<IGridFunction<double>> _input;
        private readonly ISupplier<double> _threshold;
        private readonly ISupplier<double> _minGroupSize;
        private readonly ISupplier<double> _maxGroupSize;
        private readonly double _fallback;
        private readonly int _gridSize;

        public Output(
            ISupplier<IGridFunction<double>> input,
            ISupplier<double> threshold,
            ISupplier<double> minGroupSize,
            ISupplier<double> maxGroupSize,
            double fallback, int gridSize)
        {
            _input = input;
            _threshold = threshold;
            _minGroupSize = minGroupSize;
            _maxGroupSize = maxGroupSize;
            _fallback = fallback;
            _gridSize = gridSize;
        }

        public IGridFunction<double> Get()
        {
            var input = _input.Get();
            var threshold = _threshold.Get();
            var minGroupSize = _minGroupSize.Get();
            var maxGroupSize = _maxGroupSize.Get();

            Vector2i[] offsets = [Vector2i.AxisX, Vector2i.AxisZ, -Vector2i.AxisX, -Vector2i.AxisZ];

            double[,] grid = new double[_gridSize, _gridSize];
            byte[,] state = new byte[_gridSize, _gridSize];

            // 0 = not visited
            // 1 = potential pass
            // 2 = determined pass
            // 3 = determined fail

            var group = new List<Vector2i>(10);

            for (int x = 0; x < _gridSize; x++)
            {
                for (int z = 0; z < _gridSize; z++)
                {
                    if (state[x, z] == 0)
                    {
                        var value = grid[x, z] = input.ValueAt(x, z);
                        state[x, z] = (byte) (value >= threshold ? 1 : 3);
                    }

                    if (state[x, z] == 1)
                    {
                        group.Add(new Vector2i(x, z));

                        for (int i = 0; i < group.Count; i++)
                        {
                            var p = group[i];

                            foreach (var offset in offsets)
                            {
                                var n = p + offset;

                                if (n.InBounds(Vector2i.Zero, new Vector2i(_gridSize)))
                                {
                                    if (state[n.x, n.z] == 0)
                                    {
                                        var value = grid[n.x, n.z] = input.ValueAt(n.x, n.z);
                                        state[n.x, n.z] = (byte) (value >= threshold ? 1 : 3);

                                        if (state[n.x, n.z] == 1)
                                        {
                                            group.Add(n);
                                        }
                                    }
                                }
                            }
                        }

                        var groupValid = group.Count >= minGroupSize && (maxGroupSize <= 0 || group.Count <= maxGroupSize);

                        foreach (var pos in group) state[pos.x, pos.z] = (byte) (groupValid ? 2 : 3);

                        group.Clear();
                    }
                }
            }

            for (int x = 0; x < _gridSize; x++)
            {
                for (int z = 0; z < _gridSize; z++)
                {
                    if (state[x, z] != 2) grid[x, z] = _fallback;
                }
            }

            return new GridFunction.Cache<double>(grid, _fallback);
        }

        public void ResetState()
        {
            _input.ResetState();
            _threshold.ResetState();
            _minGroupSize.ResetState();
            _maxGroupSize.ResetState();
        }
    }
}
