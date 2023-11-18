using System;
using NodeEditorFramework;
using NodeEditorFramework.Utilities;
using UnityEngine;
using static TerrainGraph.GridFunction;

namespace TerrainGraph;

[Serializable]
[Node(false, "Path/Trace", 608)]
public class NodePathTrace : NodeBase
{
    public const string ID = "pathTrace";
    public override string GetID => ID;

    public override string Title => "Path: Trace";

    [ValueConnectionKnob("Input", Direction.In, PathFunctionConnection.Id)]
    public ValueConnectionKnob InputKnob;

    [ValueConnectionKnob("Main Output", Direction.Out, GridFunctionConnection.Id)]
    public ValueConnectionKnob MainOutputKnob;

    [ValueConnectionKnob("Depth Output", Direction.Out, GridFunctionConnection.Id)]
    public ValueConnectionKnob DepthOutputKnob;

    [ValueConnectionKnob("Offset Output", Direction.Out, GridFunctionConnection.Id)]
    public ValueConnectionKnob OffsetOutputKnob;

    public double StepSize = 1;

    public override void NodeGUI()
    {
        GUILayout.BeginVertical(BoxStyle);

        GUILayout.BeginHorizontal(BoxStyle);
        GUILayout.Label("Main Grid", BoxLayout);
        InputKnob.SetPosition();
        MainOutputKnob.SetPosition();
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal(BoxStyle);
        GUILayout.Label("Depth Grid", BoxLayout);
        DepthOutputKnob.SetPosition();
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal(BoxStyle);
        GUILayout.Label("Offset Grid", BoxLayout);
        OffsetOutputKnob.SetPosition();
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal(BoxStyle);
        GUILayout.Label("Step Size", BoxLayout);
        GUILayout.FlexibleSpace();
        StepSize = RTEditorGUI.FloatField(GUIContent.none, (float) StepSize, BoxLayout);
        GUILayout.EndHorizontal();

        GUILayout.EndVertical();

        if (GUI.changed)
            canvas.OnNodeChange(this);
    }

    public override bool Calculate()
    {
        var output = new Output(
            SupplierOrFixed(InputKnob, Path.Empty),
            TerrainCanvas.GridFullSize,
            StepSize
        );

        MainOutputKnob.SetValue<ISupplier<IGridFunction<double>>>(Supplier.From(output.GetMainGrid, output.ResetState));
        DepthOutputKnob.SetValue<ISupplier<IGridFunction<double>>>(Supplier.From(output.GetDepthGrid, output.ResetState));
        OffsetOutputKnob.SetValue<ISupplier<IGridFunction<double>>>(Supplier.From(output.GetOffsetGrid, output.ResetState));

        return true;
    }

    private class Output
    {
        private readonly ISupplier<Path> _input;
        private readonly int _gridSize;
        private readonly double _stepSize;

        private PathTracer _tracer;

        public Output(ISupplier<Path> input, int gridSize, double stepSize)
        {
            _input = input;
            _gridSize = gridSize;
            _stepSize = stepSize;
        }

        private PathTracer Generate()
        {
            var tracer = new PathTracer(_gridSize, _gridSize, _stepSize);
            tracer.Trace(_input.Get());
            return tracer;
        }

        public IGridFunction<double> GetMainGrid()
        {
            _tracer ??= Generate();
            return new Cache<double>(_tracer.MainGrid);
        }

        public IGridFunction<double> GetDepthGrid()
        {
            _tracer ??= Generate();
            return new Cache<double>(_tracer.DepthGrid);
        }

        public IGridFunction<double> GetOffsetGrid()
        {
            _tracer ??= Generate();
            return new Cache<double>(_tracer.OffsetGrid);
        }

        public void ResetState()
        {
            _input.ResetState();
            _tracer = null;
        }
    }
}
