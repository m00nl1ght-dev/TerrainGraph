using System;
using NodeEditorFramework;
using UnityEngine;

namespace TerrainGraph;

[Serializable]
[Node(false, "Path/Follow", 607)]
public class NodePathFollow : NodeBase
{
    public const string ID = "pathFollow";
    public override string GetID => ID;

    public override string Title => "Path: Follow";

    [ValueConnectionKnob("Input", Direction.In, PathFunctionConnection.Id)]
    public ValueConnectionKnob InputKnob;

    [ValueConnectionKnob("Absolute Grid", Direction.In, GridFunctionConnection.Id)]
    public ValueConnectionKnob AbsGridKnob;

    [ValueConnectionKnob("Relative Grid", Direction.In, GridFunctionConnection.Id)]
    public ValueConnectionKnob RelGridKnob;

    [ValueConnectionKnob("Avoid Overlap", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob AvoidOverlapKnob;

    [ValueConnectionKnob("Output", Direction.Out, PathFunctionConnection.Id)]
    public ValueConnectionKnob OutputKnob;

    public double AvoidOverlap;

    public override void NodeGUI()
    {
        InputKnob.SetPosition(FirstKnobPosition);
        OutputKnob.SetPosition(FirstKnobPosition);

        GUILayout.BeginVertical(BoxStyle);

        GUILayout.BeginHorizontal(BoxStyle);
        GUILayout.Label("Input", BoxLayout);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal(BoxStyle);
        GUILayout.Label("Absolute Grid", BoxLayout);
        GUILayout.EndHorizontal();

        AbsGridKnob.SetPosition();

        GUILayout.BeginHorizontal(BoxStyle);
        GUILayout.Label("Relative Grid", BoxLayout);
        GUILayout.EndHorizontal();

        RelGridKnob.SetPosition();

        KnobValueField(AvoidOverlapKnob, ref AvoidOverlap);

        GUILayout.EndVertical();

        if (GUI.changed)
            canvas.OnNodeChange(this);
    }

    public override void RefreshPreview()
    {
        var avoidOverlap = GetIfConnected<double>(AvoidOverlapKnob);

        avoidOverlap?.ResetState();

        if (avoidOverlap != null) AvoidOverlap = avoidOverlap.Get();
    }

    public override bool Calculate()
    {
        OutputKnob.SetValue<ISupplier<Path>>(new Output(
            SupplierOrFallback(InputKnob, Path.Empty),
            SupplierOrFallback(AbsGridKnob, GridFunction.Zero),
            SupplierOrFallback(RelGridKnob, GridFunction.Zero),
            SupplierOrFallback(AvoidOverlapKnob, AvoidOverlap)
        ));
        return true;
    }

    private class Output : ISupplier<Path>
    {
        private readonly ISupplier<Path> _input;
        private readonly ISupplier<IGridFunction<double>> _absGrid;
        private readonly ISupplier<IGridFunction<double>> _relGrid;
        private readonly ISupplier<double> _avoidOverlap;

        public Output(
            ISupplier<Path> input,
            ISupplier<IGridFunction<double>> absGrid,
            ISupplier<IGridFunction<double>> relGrid,
            ISupplier<double> avoidOverlap)
        {
            _input = input;
            _absGrid = absGrid;
            _relGrid = relGrid;
            _avoidOverlap = avoidOverlap;
        }

        public Path Get()
        {
            var path = new Path(_input.Get());

            foreach (var segment in path.Leaves())
            {
                var extParams = segment.ExtendParams;

                extParams.AbsFollowGrid = _absGrid.Get();
                extParams.RelFollowGrid = _relGrid.Get();
                extParams.AvoidOverlap = _avoidOverlap.Get();

                segment.ExtendWithParams(extParams);
            }

            return path;
        }

        public void ResetState()
        {
            _input.ResetState();
            _absGrid.ResetState();
            _relGrid.ResetState();
            _avoidOverlap.ResetState();
        }
    }
}
