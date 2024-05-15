using System;
using System.Linq;
using NodeEditorFramework;
using TerrainGraph.Flow;
using UnityEngine;

namespace TerrainGraph;

[Serializable]
[Node(false, "Path/Density", 605)]
public class NodePathDensity : NodeBase
{
    public const string ID = "pathDensity";
    public override string GetID => ID;

    public override string Title => "Path: Density";

    [ValueConnectionKnob("Input", Direction.In, PathFunctionConnection.Id)]
    public ValueConnectionKnob InputKnob;

    [ValueConnectionKnob("Density Left Grid", Direction.In, GridFunctionConnection.Id)]
    public ValueConnectionKnob DensityLeftGridKnob;

    [ValueConnectionKnob("Density Right Grid", Direction.In, GridFunctionConnection.Id)]
    public ValueConnectionKnob DensityRightGridKnob;

    [ValueConnectionKnob("Density Loss", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob DensityLossKnob;

    [ValueConnectionKnob("Output", Direction.Out, PathFunctionConnection.Id)]
    public ValueConnectionKnob OutputKnob;

    public double DensityLoss;

    public override void NodeGUI()
    {
        InputKnob.SetPosition(FirstKnobPosition);
        OutputKnob.SetPosition(FirstKnobPosition);

        GUILayout.BeginVertical(BoxStyle);

        GUILayout.BeginHorizontal(BoxStyle);
        GUILayout.Label("Input", BoxLayout);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal(BoxStyle);
        GUILayout.Label("Density Left", BoxLayout);
        GUILayout.EndHorizontal();

        DensityLeftGridKnob.SetPosition();

        GUILayout.BeginHorizontal(BoxStyle);
        GUILayout.Label("Density Right", BoxLayout);
        GUILayout.EndHorizontal();

        DensityRightGridKnob.SetPosition();

        KnobValueField(DensityLossKnob, ref DensityLoss);

        GUILayout.EndVertical();

        if (GUI.changed)
            canvas.OnNodeChange(this);
    }

    public override void RefreshPreview()
    {
        var densityLoss = GetIfConnected<double>(DensityLossKnob);

        densityLoss?.ResetState();

        if (densityLoss != null) DensityLoss = densityLoss.Get();
    }

    public override bool Calculate()
    {
        OutputKnob.SetValue<ISupplier<Path>>(new Output(
            SupplierOrFallback(InputKnob, Path.Empty),
            SupplierOrFallback(DensityLeftGridKnob, GridFunction.One),
            SupplierOrFallback(DensityRightGridKnob, GridFunction.One),
            SupplierOrFallback(DensityLossKnob, DensityLoss)
        ));
        return true;
    }

    private class Output : ISupplier<Path>
    {
        private readonly ISupplier<Path> _input;
        private readonly ISupplier<IGridFunction<double>> _densityLeftGrid;
        private readonly ISupplier<IGridFunction<double>> _densityRightGrid;
        private readonly ISupplier<double> _densityLoss;

        public Output(
            ISupplier<Path> input,
            ISupplier<IGridFunction<double>> densityLeftGrid,
            ISupplier<IGridFunction<double>> densityRightGrid,
            ISupplier<double> densityLoss)
        {
            _input = input;
            _densityLeftGrid = densityLeftGrid;
            _densityRightGrid = densityRightGrid;
            _densityLoss = densityLoss;
        }

        public Path Get()
        {
            var path = new Path(_input.Get());

            foreach (var segment in path.Leaves.ToList())
            {
                var extParams = segment.TraceParams;

                extParams.DensityLeftGrid = _densityLeftGrid.Get();
                extParams.DensityRightGrid = _densityRightGrid.Get();
                extParams.DensityLoss = _densityLoss.Get();

                segment.ExtendWithParams(extParams);
            }

            return path;
        }

        public void ResetState()
        {
            _input.ResetState();
            _densityLeftGrid.ResetState();
            _densityRightGrid.ResetState();
            _densityLoss.ResetState();
        }
    }
}
