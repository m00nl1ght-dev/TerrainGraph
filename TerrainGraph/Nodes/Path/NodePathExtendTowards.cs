using System;
using System.Linq;
using NodeEditorFramework;
using TerrainGraph.Flow;
using TerrainGraph.Util;
using UnityEngine;

namespace TerrainGraph;

[Serializable]
[Node(false, "Path/Extend Towards", 602)]
public class NodePathExtendTowards : NodeBase
{
    public const string ID = "pathExtendTowards";
    public override string GetID => ID;

    public override string Title => "Path: Extend Towards";

    [ValueConnectionKnob("Input", Direction.In, PathFunctionConnection.Id)]
    public ValueConnectionKnob InputKnob;

    [ValueConnectionKnob("Target X", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob TargetXKnob;

    [ValueConnectionKnob("Target Z", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob TargetZKnob;

    [ValueConnectionKnob("Length", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob LengthKnob;

    [ValueConnectionKnob("Step size", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob StepSizeKnob;

    [ValueConnectionKnob("Tenacity", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob TenacityKnob;

    [ValueConnectionKnob("Split tenacity", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob SplitTenacityKnob;

    [ValueConnectionKnob("Output", Direction.Out, PathFunctionConnection.Id)]
    public ValueConnectionKnob OutputKnob;

    public double TargetX;
    public double TargetZ;
    public double Length = 100;
    public double StepSize = 5;
    public double Tenacity;
    public double SplitTenacity;

    public override void NodeGUI()
    {
        InputKnob.SetPosition(FirstKnobPosition);
        OutputKnob.SetPosition(FirstKnobPosition);

        GUILayout.BeginVertical(BoxStyle);

        GUILayout.BeginHorizontal(BoxStyle);
        GUILayout.Label("Input", BoxLayout);
        GUILayout.EndHorizontal();

        KnobValueField(TargetXKnob, ref TargetX);
        KnobValueField(TargetZKnob, ref TargetZ);
        KnobValueField(LengthKnob, ref Length);
        KnobValueField(StepSizeKnob, ref StepSize);
        KnobValueField(TenacityKnob, ref Tenacity);
        KnobValueField(SplitTenacityKnob, ref SplitTenacity);

        GUILayout.EndVertical();

        if (GUI.changed)
            canvas.OnNodeChange(this);
    }

    public override void RefreshPreview()
    {
        var targetX = GetIfConnected<double>(TargetXKnob);
        var targetZ = GetIfConnected<double>(TargetZKnob);
        var length = GetIfConnected<double>(LengthKnob);
        var stepSize = GetIfConnected<double>(StepSizeKnob);
        var tenacity = GetIfConnected<double>(TenacityKnob);
        var splitTenacity = GetIfConnected<double>(SplitTenacityKnob);

        targetX?.ResetState();
        targetZ?.ResetState();
        length?.ResetState();
        stepSize?.ResetState();
        tenacity?.ResetState();
        splitTenacity?.ResetState();

        if (targetX != null) TargetX = targetX.Get();
        if (targetZ != null) TargetZ = targetZ.Get();
        if (length != null) Length = length.Get();
        if (stepSize != null) StepSize = stepSize.Get();
        if (tenacity != null) Tenacity = tenacity.Get();
        if (splitTenacity != null) SplitTenacity = splitTenacity.Get();
    }

    public override bool Calculate()
    {
        OutputKnob.SetValue<ISupplier<Path>>(new Output(
            SupplierOrFallback(InputKnob, Path.Empty),
            SupplierOrFallback(TargetXKnob, TargetX),
            SupplierOrFallback(TargetZKnob, TargetZ),
            SupplierOrFallback(LengthKnob, Length),
            SupplierOrFallback(StepSizeKnob, StepSize),
            SupplierOrFallback(TenacityKnob, Tenacity),
            SupplierOrFallback(SplitTenacityKnob, SplitTenacity),
            TerrainCanvas.GridFullSize
        ));
        return true;
    }

    private class Output : ISupplier<Path>
    {
        private readonly ISupplier<Path> _input;
        private readonly ISupplier<double> _targetX;
        private readonly ISupplier<double> _targetZ;
        private readonly ISupplier<double> _length;
        private readonly ISupplier<double> _stepSize;
        private readonly ISupplier<double> _tenacity;
        private readonly ISupplier<double> _splitTenacity;
        private readonly int _gridSize;

        public Output(
            ISupplier<Path> input, ISupplier<double> targetX, ISupplier<double> targetZ,
            ISupplier<double> length, ISupplier<double> stepSize, ISupplier<double> tenacity,
            ISupplier<double> splitTenacity, int gridSize)
        {
            _input = input;
            _targetX = targetX;
            _targetZ = targetZ;
            _length = length;
            _stepSize = stepSize;
            _tenacity = tenacity;
            _splitTenacity = splitTenacity;
            _gridSize = gridSize;
        }

        public Path Get()
        {
            var path = new Path(_input.Get());

            foreach (var segment in path.Leaves.ToList())
            {
                var targetX = _targetX.Get();
                var targetZ = _targetZ.Get();
                var length = _length.Get().WithMin(0);
                var stepSize = _stepSize.Get().WithMin(1);
                var tenacity = _tenacity.Get().InRange01();
                var splitTenacity = _splitTenacity.Get().InRange01();

                var extParams = segment.TraceParams;

                extParams.StepSize = stepSize;
                extParams.AngleTenacity = tenacity;
                extParams.SplitTenacity = splitTenacity;
                extParams.Target = new Vector2d(targetX, targetZ) * _gridSize;

                segment.ExtendWithParams(extParams, length);
            }

            return path;
        }

        public void ResetState()
        {
            _input.ResetState();
            _targetX.ResetState();
            _targetZ.ResetState();
            _length.ResetState();
            _stepSize.ResetState();
            _tenacity.ResetState();
            _splitTenacity.ResetState();
        }
    }
}
