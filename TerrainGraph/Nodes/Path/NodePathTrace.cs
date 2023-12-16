using System;
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
        var output = new Output(
            SupplierOrFallback(InputKnob, Path.Empty),
            TerrainCanvas.GridFullSize
        );

        MainOutputKnob.SetValue<ISupplier<IGridFunction<double>>>(Supplier.From(output.GetMainGrid, output.ResetState));
        ValueOutputKnob.SetValue<ISupplier<IGridFunction<double>>>(Supplier.From(output.GetValueGrid, output.ResetState));
        OffsetOutputKnob.SetValue<ISupplier<IGridFunction<double>>>(Supplier.From(output.GetOffsetGrid, output.ResetState));
        DistanceOutputKnob.SetValue<ISupplier<IGridFunction<double>>>(Supplier.From(output.GetDistanceGrid, output.ResetState));

        return true;
    }

    private class Output
    {
        private readonly ISupplier<Path> _input;
        private readonly int _gridSize;

        private PathTracer _tracer;

        public Output(ISupplier<Path> input, int gridSize)
        {
            _input = input;
            _gridSize = gridSize;
        }

        private PathTracer Generate()
        {
            var tracer = new PathTracer(
                _gridSize, _gridSize,
                GridMarginDefault,
                TraceMarginInnerDefault,
                TraceMarginOuterDefault
            );

            tracer.Trace(_input.Get());

            return tracer;
        }

        public IGridFunction<double> GetMainGrid()
        {
            _tracer ??= Generate();
            return _tracer.MainGrid;
        }

        public IGridFunction<double> GetValueGrid()
        {
            _tracer ??= Generate();
            return _tracer.ValueGrid;
        }

        public IGridFunction<double> GetOffsetGrid()
        {
            _tracer ??= Generate();
            return _tracer.OffsetGrid;
        }

        public IGridFunction<double> GetDistanceGrid()
        {
            _tracer ??= Generate();
            return _tracer.DistanceGrid;
        }

        public void ResetState()
        {
            _input.ResetState();
            _tracer = null; // TODO investigate: this might cause it to be generated multiple times per map gen
        }
    }
}
