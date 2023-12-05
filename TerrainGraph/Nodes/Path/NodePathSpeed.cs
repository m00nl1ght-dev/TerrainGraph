using System;
using NodeEditorFramework;
using UnityEngine;

namespace TerrainGraph;

[Serializable]
[Node(false, "Path/Speed", 604)]
public class NodePathSpeed : NodeBase
{
    public const string ID = "pathSpeed";
    public override string GetID => ID;

    public override string Title => "Path: Speed";

    [ValueConnectionKnob("Input", Direction.In, PathFunctionConnection.Id)]
    public ValueConnectionKnob InputKnob;

    [ValueConnectionKnob("Speed Grid", Direction.In, GridFunctionConnection.Id)]
    public ValueConnectionKnob SpeedGridKnob;

    [ValueConnectionKnob("Speed Loss", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob SpeedLossKnob;

    [ValueConnectionKnob("Output", Direction.Out, PathFunctionConnection.Id)]
    public ValueConnectionKnob OutputKnob;

    public double SpeedLoss;

    public override void NodeGUI()
    {
        InputKnob.SetPosition(FirstKnobPosition);
        OutputKnob.SetPosition(FirstKnobPosition);

        GUILayout.BeginVertical(BoxStyle);

        GUILayout.BeginHorizontal(BoxStyle);
        GUILayout.Label("Input", BoxLayout);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal(BoxStyle);
        GUILayout.Label("Speed Grid", BoxLayout);
        GUILayout.EndHorizontal();

        SpeedGridKnob.SetPosition();

        KnobValueField(SpeedLossKnob, ref SpeedLoss);

        GUILayout.EndVertical();

        if (GUI.changed)
            canvas.OnNodeChange(this);
    }

    public override void RefreshPreview()
    {
        var speedLoss = GetIfConnected<double>(SpeedLossKnob);

        speedLoss?.ResetState();

        if (speedLoss != null) SpeedLoss = speedLoss.Get();
    }

    public override bool Calculate()
    {
        OutputKnob.SetValue<ISupplier<Path>>(new Output(
            SupplierOrFallback(InputKnob, Path.Empty),
            SupplierOrFallback(SpeedGridKnob, GridFunction.One),
            SupplierOrFallback(SpeedLossKnob, SpeedLoss)
        ));
        return true;
    }

    private class Output : ISupplier<Path>
    {
        private readonly ISupplier<Path> _input;
        private readonly ISupplier<IGridFunction<double>> _speedGrid;
        private readonly ISupplier<double> _speedLoss;

        public Output(ISupplier<Path> input, ISupplier<IGridFunction<double>> speedGrid, ISupplier<double> speedLoss)
        {
            _input = input;
            _speedGrid = speedGrid;
            _speedLoss = speedLoss;
        }

        public Path Get()
        {
            var path = new Path(_input.Get());

            foreach (var segment in path.Leaves())
            {
                var extParams = segment.ExtendParams;

                extParams.SpeedGrid = _speedGrid.Get();
                extParams.SpeedLoss = _speedLoss.Get();

                segment.ExtendWithParams(extParams);
            }

            return path;
        }

        public void ResetState()
        {
            _input.ResetState();
            _speedGrid.ResetState();
            _speedLoss.ResetState();
        }
    }
}
