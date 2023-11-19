using System;
using NodeEditorFramework;
using UnityEngine;

namespace TerrainGraph;

[Serializable]
[Node(false, "Path/Width", 607)]
public class NodePathWidth : NodeBase
{
    public const string ID = "pathWidth";
    public override string GetID => ID;

    public override string Title => "Path: Width";

    [ValueConnectionKnob("Input", Direction.In, PathFunctionConnection.Id)]
    public ValueConnectionKnob InputKnob;

    [ValueConnectionKnob("Width Grid", Direction.In, GridFunctionConnection.Id)]
    public ValueConnectionKnob WidthGridKnob;

    [ValueConnectionKnob("Width Loss", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob WidthLossKnob;

    [ValueConnectionKnob("Output", Direction.Out, PathFunctionConnection.Id)]
    public ValueConnectionKnob OutputKnob;

    public double WidthLoss;

    public override void NodeGUI()
    {
        InputKnob.SetPosition(FirstKnobPosition);
        OutputKnob.SetPosition(FirstKnobPosition);

        GUILayout.BeginVertical(BoxStyle);

        GUILayout.BeginHorizontal(BoxStyle);
        GUILayout.Label("Input", BoxLayout);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal(BoxStyle);
        GUILayout.Label("Width Grid", BoxLayout);
        GUILayout.EndHorizontal();

        WidthGridKnob.SetPosition();

        KnobValueField(WidthLossKnob, ref WidthLoss);

        GUILayout.EndVertical();

        if (GUI.changed)
            canvas.OnNodeChange(this);
    }

    public override void RefreshPreview()
    {
        var widthLoss = GetIfConnected<double>(WidthLossKnob);

        widthLoss?.ResetState();

        if (widthLoss != null) WidthLoss = widthLoss.Get();
    }

    public override bool Calculate()
    {
        OutputKnob.SetValue<ISupplier<Path>>(new Output(
            SupplierOrFixed(InputKnob, Path.Empty),
            SupplierOrGridFixed(WidthGridKnob, GridFunction.One),
            SupplierOrValueFixed(WidthLossKnob, WidthLoss)
        ));
        return true;
    }

    private class Output : ISupplier<Path>
    {
        private readonly ISupplier<Path> _input;
        private readonly ISupplier<IGridFunction<double>> _widthGrid;
        private readonly ISupplier<double> _widthLoss;

        public Output(ISupplier<Path> input, ISupplier<IGridFunction<double>> widthGrid, ISupplier<double> widthLoss)
        {
            _input = input;
            _widthGrid = widthGrid;
            _widthLoss = widthLoss;
        }

        public Path Get()
        {
            var path = new Path(_input.Get());

            foreach (var segment in path.Leaves())
            {
                var extParams = segment.ExtendParams;

                extParams.WidthGrid = _widthGrid.Get();
                extParams.WidthLoss = _widthLoss.Get();

                segment.ExtendWithParams(extParams);
            }

            return path;
        }

        public void ResetState()
        {
            _input.ResetState();
            _widthGrid.ResetState();
            _widthLoss.ResetState();
        }
    }
}
