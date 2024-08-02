using System;
using System.Linq;
using NodeEditorFramework;
using TerrainGraph.Flow;
using UnityEngine;

namespace TerrainGraph;

[Serializable]
[Node(false, "Path/Loss", 603)]
public class NodePathLoss : NodeBase
{
    public const string ID = "pathLoss";
    public override string GetID => ID;

    public override string Title => "Path: Loss";

    [ValueConnectionKnob("Input", Direction.In, PathFunctionConnection.Id)]
    public ValueConnectionKnob InputKnob;

    [ValueConnectionKnob("Width loss", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob WidthLossKnob;

    [ValueConnectionKnob("Density loss", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob DensityLossKnob;

    [ValueConnectionKnob("Speed loss", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob SpeedLossKnob;

    [ValueConnectionKnob("Output", Direction.Out, PathFunctionConnection.Id)]
    public ValueConnectionKnob OutputKnob;

    public double WidthLoss;
    public double DensityLoss;
    public double SpeedLoss;

    public override void NodeGUI()
    {
        InputKnob.SetPosition(FirstKnobPosition);
        OutputKnob.SetPosition(FirstKnobPosition);

        GUILayout.BeginVertical(BoxStyle);

        GUILayout.BeginHorizontal(BoxStyle);
        GUILayout.Label("Input", BoxLayout);
        GUILayout.EndHorizontal();

        KnobValueField(WidthLossKnob, ref WidthLoss);
        KnobValueField(DensityLossKnob, ref DensityLoss);
        KnobValueField(SpeedLossKnob, ref SpeedLoss);

        GUILayout.EndVertical();

        if (GUI.changed)
            canvas.OnNodeChange(this);
    }

    public override void RefreshPreview()
    {
        var widthLoss = GetIfConnected<double>(WidthLossKnob);
        var densityLoss = GetIfConnected<double>(DensityLossKnob);
        var speedLoss = GetIfConnected<double>(SpeedLossKnob);

        widthLoss?.ResetState();
        densityLoss?.ResetState();
        speedLoss?.ResetState();

        if (widthLoss != null) WidthLoss = widthLoss.Get();
        if (densityLoss != null) DensityLoss = densityLoss.Get();
        if (speedLoss != null) SpeedLoss = speedLoss.Get();
    }

    public override void CleanUpGUI()
    {
        if (WidthLossKnob.connected()) WidthLoss = 0;
        if (DensityLossKnob.connected()) DensityLoss = 0;
        if (SpeedLossKnob.connected()) SpeedLoss = 0;
    }

    public override bool Calculate()
    {
        OutputKnob.SetValue<ISupplier<Path>>(new Output(
            SupplierOrFallback(InputKnob, Path.Empty),
            SupplierOrFallback(WidthLossKnob, WidthLoss),
            SupplierOrFallback(DensityLossKnob, DensityLoss),
            SupplierOrFallback(SpeedLossKnob, SpeedLoss)
        ));
        return true;
    }

    private class Output : ISupplier<Path>
    {
        private readonly ISupplier<Path> _input;
        private readonly ISupplier<double> _widthLoss;
        private readonly ISupplier<double> _densityLoss;
        private readonly ISupplier<double> _speedLoss;

        public Output(
            ISupplier<Path> input,
            ISupplier<double> widthLoss,
            ISupplier<double> densityLoss,
            ISupplier<double> speedLoss)
        {
            _input = input;
            _widthLoss = widthLoss;
            _densityLoss = densityLoss;
            _speedLoss = speedLoss;
        }

        public Path Get()
        {
            var path = new Path(_input.Get());

            foreach (var segment in path.Leaves.ToList())
            {
                var widthLoss = _widthLoss.Get();
                var densityLoss = _densityLoss.Get();
                var speedLoss = _speedLoss.Get();

                var extParams = segment.TraceParams;

                extParams.WidthLoss = widthLoss;
                extParams.DensityLoss = densityLoss;
                extParams.SpeedLoss = speedLoss;

                segment.ExtendWithParams(extParams);
            }

            return path;
        }

        public void ResetState()
        {
            _input.ResetState();
            _widthLoss.ResetState();
            _densityLoss.ResetState();
            _speedLoss.ResetState();
        }
    }
}
