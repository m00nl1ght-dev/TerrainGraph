using System;
using System.Collections.Generic;
using NodeEditorFramework;
using UnityEngine;

namespace TerrainGraph;

[Serializable]
[Node(false, "Path/Trace", 621)]
public class NodePathTrace : NodeBase
{
    public const int GridMarginDefault = 3;

    public const double TraceMarginInnerDefault = 3;
    public const double TraceMarginOuterDefault = 50;

    public const string ID = "pathTrace";
    public override string GetID => ID;

    public override string Title => "Path: Trace";

    public static IGridFunction<double> DebugGrid;
    public static List<PathTracer.DebugLine> DebugLines;

    [ValueConnectionKnob("Input", Direction.In, PathFunctionConnection.Id)]
    public ValueConnectionKnob InputKnob;

    [ValueConnectionKnob("Main Output", Direction.Out, GridFunctionConnection.Id)]
    public ValueConnectionKnob MainOutputKnob;

    [ValueConnectionKnob("Value Output", Direction.Out, GridFunctionConnection.Id)]
    public ValueConnectionKnob ValueOutputKnob;

    [ValueConnectionKnob("Offset Output", Direction.Out, GridFunctionConnection.Id)]
    public ValueConnectionKnob OffsetOutputKnob;

    [ValueConnectionKnob("Distance Output", Direction.Out, GridFunctionConnection.Id)]
    public ValueConnectionKnob DistanceOutputKnob;

    public override void NodeGUI()
    {
        GUILayout.BeginVertical(BoxStyle);

        GUILayout.BeginHorizontal(BoxStyle);
        GUILayout.Label("Main Grid", BoxLayout);
        InputKnob.SetPosition();
        MainOutputKnob.SetPosition();
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal(BoxStyle);
        GUILayout.Label("Value Grid", BoxLayout);
        ValueOutputKnob.SetPosition();
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal(BoxStyle);
        GUILayout.Label("Offset Grid", BoxLayout);
        OffsetOutputKnob.SetPosition();
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal(BoxStyle);
        GUILayout.Label("Distance Grid", BoxLayout);
        DistanceOutputKnob.SetPosition();
        GUILayout.EndHorizontal();

        GUILayout.EndVertical();

        if (GUI.changed)
            canvas.OnNodeChange(this);
    }

    public override bool Calculate()
    {
        var cache = new List<PathTracer>(5);

        var output = new Output(
            SupplierOrFallback(InputKnob, Path.Empty),
            TerrainCanvas.GridFullSize,
            !TerrainCanvas.HasActiveGUI
        );

        MainOutputKnob.SetValue<ISupplier<IGridFunction<double>>>(
            new Supplier.CompoundCached<PathTracer,IGridFunction<double>>(output, t => t.MainGrid, cache)
        );

        ValueOutputKnob.SetValue<ISupplier<IGridFunction<double>>>(
            new Supplier.CompoundCached<PathTracer,IGridFunction<double>>(output, t => t.ValueGrid, cache)
        );

        OffsetOutputKnob.SetValue<ISupplier<IGridFunction<double>>>(
            new Supplier.CompoundCached<PathTracer,IGridFunction<double>>(output, t => t.OffsetGrid, cache)
        );

        DistanceOutputKnob.SetValue<ISupplier<IGridFunction<double>>>(
            new Supplier.CompoundCached<PathTracer,IGridFunction<double>>(output, t => t.DistanceGrid, cache)
        );

        return true;
    }

    private class Output : ISupplier<PathTracer>
    {
        private readonly ISupplier<Path> _input;
        private readonly int _gridSize;
        private readonly bool _debug;

        public Output(ISupplier<Path> input, int gridSize, bool debug)
        {
            _input = input;
            _gridSize = gridSize;
            _debug = debug;
        }

        public PathTracer Get()
        {
            var tracer = new PathTracer(
                _gridSize, _gridSize,
                GridMarginDefault,
                TraceMarginInnerDefault,
                TraceMarginOuterDefault
            );

            var maxAttempts = 50;

            if (_debug)
            {
                tracer.DebugLines = DebugLines;

                if (Input.GetKey(KeyCode.Alpha1)) maxAttempts = 1;
                if (Input.GetKey(KeyCode.Alpha2)) maxAttempts = 2;
                if (Input.GetKey(KeyCode.Alpha3)) maxAttempts = 3;
                if (Input.GetKey(KeyCode.Alpha4)) maxAttempts = 4;
                if (Input.GetKey(KeyCode.Alpha5)) maxAttempts = 5;
                if (Input.GetKey(KeyCode.Alpha6)) maxAttempts = 6;
                if (Input.GetKey(KeyCode.Alpha7)) maxAttempts = 7;
                if (Input.GetKey(KeyCode.Alpha8)) maxAttempts = 8;
                if (Input.GetKey(KeyCode.Alpha9)) maxAttempts = 9;
            }

            tracer.Trace(_input.Get(), maxAttempts);

            if (_debug)
            {
                DebugGrid = tracer.DebugGrid;
            }

            return tracer;
        }

        public void ResetState()
        {
            _input.ResetState();
        }
    }
}
