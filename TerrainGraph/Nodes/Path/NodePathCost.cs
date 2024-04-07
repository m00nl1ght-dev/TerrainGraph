using System;
using System.Linq;
using NodeEditorFramework;
using TerrainGraph.Flow;
using UnityEngine;

namespace TerrainGraph;

[Serializable]
[Node(false, "Path/Cost", 607)]
public class NodePathCost : NodeBase
{
    public const string ID = "pathCost";
    public override string GetID => ID;

    public override string Title => "Path: Cost";

    [ValueConnectionKnob("Input", Direction.In, PathFunctionConnection.Id)]
    public ValueConnectionKnob InputKnob;

    [ValueConnectionKnob("Cost Grid", Direction.In, GridFunctionConnection.Id)]
    public ValueConnectionKnob CostGridKnob;

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
        GUILayout.Label("Cost Grid", BoxLayout);
        GUILayout.EndHorizontal();

        CostGridKnob.SetPosition();

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
            SupplierOrFallback(CostGridKnob, GridFunction.Zero),
            SupplierOrFallback(AvoidOverlapKnob, AvoidOverlap)
        ));
        return true;
    }

    private class Output : ISupplier<Path>
    {
        private readonly ISupplier<Path> _input;
        private readonly ISupplier<IGridFunction<double>> _costGrid;
        private readonly ISupplier<double> _avoidOverlap;

        public Output(
            ISupplier<Path> input,
            ISupplier<IGridFunction<double>> costGrid,
            ISupplier<double> avoidOverlap)
        {
            _input = input;
            _costGrid = costGrid;
            _avoidOverlap = avoidOverlap;
        }

        public Path Get()
        {
            var path = new Path(_input.Get());

            foreach (var segment in path.Leaves.ToList())
            {
                var extParams = segment.TraceParams;

                extParams.CostGrid = _costGrid.Get();
                extParams.AvoidOverlap = _avoidOverlap.Get();

                segment.ExtendWithParams(extParams);
            }

            return path;
        }

        public void ResetState()
        {
            _input.ResetState();
            _costGrid.ResetState();
            _avoidOverlap.ResetState();
        }
    }
}
