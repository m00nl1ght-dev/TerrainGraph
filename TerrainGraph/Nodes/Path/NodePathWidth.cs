using System;
using System.Linq;
using NodeEditorFramework;
using TerrainGraph.Flow;
using UnityEngine;

namespace TerrainGraph;

[Serializable]
[Node(false, "Path/Width", 603)]
public class NodePathWidth : NodeBase
{
    public const string ID = "pathWidth";
    public override string GetID => ID;

    public override string Title => "Path: Width";

    [ValueConnectionKnob("Input", Direction.In, PathFunctionConnection.Id)]
    public ValueConnectionKnob InputKnob;

    [ValueConnectionKnob("Extent Left", Direction.In, GridFunctionConnection.Id)]
    public ValueConnectionKnob ExtentLeftGridKnob;

    [ValueConnectionKnob("Extent Right", Direction.In, GridFunctionConnection.Id)]
    public ValueConnectionKnob ExtentRightGridKnob;

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
        GUILayout.Label("Extent Left", BoxLayout);
        GUILayout.EndHorizontal();

        ExtentLeftGridKnob.SetPosition();

        GUILayout.BeginHorizontal(BoxStyle);
        GUILayout.Label("Extent Right", BoxLayout);
        GUILayout.EndHorizontal();

        ExtentRightGridKnob.SetPosition();

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
            SupplierOrFallback(InputKnob, Path.Empty),
            SupplierOrFallback(ExtentLeftGridKnob, GridFunction.One),
            SupplierOrFallback(ExtentRightGridKnob, GridFunction.One),
            SupplierOrFallback(WidthLossKnob, WidthLoss)
        ));
        return true;
    }

    private class Output : ISupplier<Path>
    {
        private readonly ISupplier<Path> _input;
        private readonly ISupplier<IGridFunction<double>> _extentLeftGrid;
        private readonly ISupplier<IGridFunction<double>> _extentRightGrid;
        private readonly ISupplier<double> _widthLoss;

        public Output(
            ISupplier<Path> input,
            ISupplier<IGridFunction<double>> extentLeftGrid,
            ISupplier<IGridFunction<double>> extentRightGrid,
            ISupplier<double> widthLoss)
        {
            _input = input;
            _extentLeftGrid = extentLeftGrid;
            _extentRightGrid = extentRightGrid;
            _widthLoss = widthLoss;
        }

        public Path Get()
        {
            var path = new Path(_input.Get());

            foreach (var segment in path.Leaves.ToList())
            {
                var extParams = segment.TraceParams;

                extParams.ExtentLeftGrid = _extentLeftGrid.Get();
                extParams.ExtentRightGrid = _extentRightGrid.Get();
                extParams.WidthLoss = _widthLoss.Get();

                segment.ExtendWithParams(extParams);
            }

            return path;
        }

        public void ResetState()
        {
            _input.ResetState();
            _extentLeftGrid.ResetState();
            _extentRightGrid.ResetState();
            _widthLoss.ResetState();
        }
    }
}
