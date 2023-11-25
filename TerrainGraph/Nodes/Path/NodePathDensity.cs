using System;
using NodeEditorFramework;
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

    [ValueConnectionKnob("Density Grid", Direction.In, GridFunctionConnection.Id)]
    public ValueConnectionKnob DensityGridKnob;

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
        GUILayout.Label("Density Grid", BoxLayout);
        GUILayout.EndHorizontal();

        DensityGridKnob.SetPosition();

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
            SupplierOrFixed(InputKnob, Path.Empty),
            SupplierOrGridFixed(DensityGridKnob, GridFunction.One),
            SupplierOrValueFixed(DensityLossKnob, DensityLoss)
        ));
        return true;
    }

    private class Output : ISupplier<Path>
    {
        private readonly ISupplier<Path> _input;
        private readonly ISupplier<IGridFunction<double>> _densityGrid;
        private readonly ISupplier<double> _densityLoss;

        public Output(ISupplier<Path> input, ISupplier<IGridFunction<double>> densityGrid, ISupplier<double> densityLoss)
        {
            _input = input;
            _densityGrid = densityGrid;
            _densityLoss = densityLoss;
        }

        public Path Get()
        {
            var path = new Path(_input.Get());

            foreach (var segment in path.Leaves())
            {
                var extParams = segment.ExtendParams;

                extParams.DensityGrid = _densityGrid.Get();
                extParams.DensityLoss = _densityLoss.Get();

                segment.ExtendWithParams(extParams);
            }

            return path;
        }

        public void ResetState()
        {
            _input.ResetState();
            _densityGrid.ResetState();
            _densityLoss.ResetState();
        }
    }
}
