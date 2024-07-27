using System;
using System.Collections.Generic;
using NodeEditorFramework;
using TerrainGraph.Flow;
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

    [ValueConnectionKnob("Thin limit", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob ThinLimitKnob;

    [ValueConnectionKnob("Output", Direction.Out, GridFunctionConnection.Id)]
    public ValueConnectionKnob OutputKnob;

    public double Threshold = 1;
    public double MinGroupSize;
    public double MaxGroupSize;
    public double ThinLimit;

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
        KnobValueField(ThinLimitKnob, ref ThinLimit);

        GUILayout.EndVertical();

        if (GUI.changed)
            canvas.OnNodeChange(this);
    }

    public override void RefreshPreview()
    {
        var threshold = GetIfConnected<double>(ThresholdKnob);
        var minGroupSize = GetIfConnected<double>(MinGroupSizeKnob);
        var maxGroupSize = GetIfConnected<double>(MaxGroupSizeKnob);
        var thinLimit = GetIfConnected<double>(ThinLimitKnob);

        threshold?.ResetState();
        minGroupSize?.ResetState();
        maxGroupSize?.ResetState();
        thinLimit?.ResetState();

        if (threshold != null) Threshold = threshold.Get();
        if (minGroupSize != null) MinGroupSize = minGroupSize.Get();
        if (maxGroupSize != null) MaxGroupSize = maxGroupSize.Get();
        if (thinLimit != null) ThinLimit = thinLimit.Get();
    }

    public override bool Calculate()
    {
        var cache = new List<IGridFunction<double>>(5);

        var output = new Output(
            SupplierOrFallback(InputKnob, GridFunction.Zero),
            SupplierOrFallback(ThresholdKnob, Threshold),
            SupplierOrFallback(MinGroupSizeKnob, MinGroupSize),
            SupplierOrFallback(MaxGroupSizeKnob, MaxGroupSize),
            SupplierOrFallback(ThinLimitKnob, ThinLimit),
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
        private readonly ISupplier<double> _thinLimit;
        private readonly double _fallback;
        private readonly int _gridSize;

        public Output(
            ISupplier<IGridFunction<double>> input,
            ISupplier<double> threshold,
            ISupplier<double> minGroupSize,
            ISupplier<double> maxGroupSize,
            ISupplier<double> thinLimit,
            double fallback, int gridSize)
        {
            _input = input;
            _threshold = threshold;
            _minGroupSize = minGroupSize;
            _maxGroupSize = maxGroupSize;
            _thinLimit = thinLimit;
            _fallback = fallback;
            _gridSize = gridSize;
        }

        public IGridFunction<double> Get()
        {
            var input = _input.Get();
            var threshold = _threshold.Get();
            var minGroupSize = _minGroupSize.Get();
            var maxGroupSize = _maxGroupSize.Get();
            var thinLimit = _thinLimit.Get();

            Vector2i[] offsets = [new(1, 0), new(-1, 0), new(0, 1), new(0, -1)];

            double[,] grid = new double[_gridSize, _gridSize];

            for (int x = 0; x < _gridSize; x++)
            {
                for (int z = 0; z < _gridSize; z++)
                {
                    grid[x, z] = input.ValueAt(x, z);
                }
            }

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
                        state[x, z] = (byte) (grid[x, z] >= threshold ? 1 : 3);
                    }

                    if (state[x, z] == 1)
                    {
                        var thinCount = 0;

                        group.Add(new Vector2i(x, z));

                        for (int i = 0; i < group.Count; i++)
                        {
                            var p = group[i];

                            var adjacentCount = 0;

                            foreach (var offset in offsets)
                            {
                                var n = p + offset;

                                if (n.InBounds(Vector2i.Zero, new Vector2i(_gridSize)))
                                {
                                    if (state[n.x, n.z] == 0)
                                    {
                                        state[n.x, n.z] = (byte) (grid[n.x, n.z] >= threshold ? 1 : 3);

                                        if (state[n.x, n.z] == 1)
                                        {
                                            adjacentCount++;
                                            group.Add(n);
                                        }
                                    }
                                    else if (state[n.x, n.z] != 3)
                                    {
                                        adjacentCount++;
                                    }
                                }
                            }

                            if (adjacentCount < 4) thinCount++;
                        }

                        var thinFactor = thinCount / (double) group.Count;

                        var groupValid = (group.Count >= minGroupSize || minGroupSize <= 0) &&
                                         (group.Count <= maxGroupSize || maxGroupSize <= 0) &&
                                         (thinFactor <= thinLimit || thinLimit <= 0);

                        foreach (var pos in group) state[pos.x, pos.z] = (byte) (groupValid ? 2 : 3);

                        #if DEBUG
                        var dPos = new Vector2d(group[0].x + 0.5, group[0].z + 0.5);
                        PathTracer.DebugLine(new TraceDebugLine(null, dPos, 1, 0, $"{thinCount} / {group.Count} = {thinFactor:F2}"));
                        #endif

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
            _thinLimit.ResetState();
        }
    }
}
