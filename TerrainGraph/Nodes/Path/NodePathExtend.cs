using System;
using NodeEditorFramework;
using TerrainGraph.Util;
using UnityEngine;

namespace TerrainGraph;

[Serializable]
[Node(false, "Path/Extend", 601)]
public class NodePathExtend : NodeBase
{
    public const string ID = "pathExtend";
    public override string GetID => ID;

    public override string Title => "Path: Extend";

    [ValueConnectionKnob("Input", Direction.In, PathFunctionConnection.Id)]
    public ValueConnectionKnob InputKnob;

    [ValueConnectionKnob("Length", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob LengthKnob;

    [ValueConnectionKnob("Step Size", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob StepSizeKnob;

    [ValueConnectionKnob("Tenacity", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob TenacityKnob;

    [ValueConnectionKnob("Output", Direction.Out, PathFunctionConnection.Id)]
    public ValueConnectionKnob OutputKnob;

    public double Length = 1;
    public double StepSize = 5;
    public double Tenacity = 0.2;

    public override void NodeGUI()
    {
        InputKnob.SetPosition(FirstKnobPosition);
        OutputKnob.SetPosition(FirstKnobPosition);

        GUILayout.BeginVertical(BoxStyle);

        GUILayout.BeginHorizontal(BoxStyle);
        GUILayout.Label("Input", BoxLayout);
        GUILayout.EndHorizontal();

        KnobValueField(LengthKnob, ref Length);
        KnobValueField(StepSizeKnob, ref StepSize);
        KnobValueField(TenacityKnob, ref Tenacity);

        GUILayout.EndVertical();

        if (GUI.changed)
            canvas.OnNodeChange(this);
    }

    public override void RefreshPreview()
    {
        var length = GetIfConnected<double>(LengthKnob);
        var stepSize = GetIfConnected<double>(StepSizeKnob);
        var tenacity = GetIfConnected<double>(TenacityKnob);

        length?.ResetState();
        stepSize?.ResetState();
        tenacity?.ResetState();

        if (length != null) Length = length.Get();
        if (stepSize != null) StepSize = stepSize.Get();
        if (tenacity != null) Tenacity = tenacity.Get();
    }

    public override bool Calculate()
    {
        OutputKnob.SetValue<ISupplier<Path>>(new Output(
            SupplierOrFallback(InputKnob, Path.Empty),
            SupplierOrFallback(LengthKnob, Length),
            SupplierOrFallback(StepSizeKnob, StepSize),
            SupplierOrFallback(TenacityKnob, Tenacity)
        ));
        return true;
    }

    private class Output : ISupplier<Path>
    {
        private readonly ISupplier<Path> _input;
        private readonly ISupplier<double> _length;
        private readonly ISupplier<double> _stepSize;
        private readonly ISupplier<double> _tenacity;

        public Output(
            ISupplier<Path> input,
            ISupplier<double> length,
            ISupplier<double> stepSize,
            ISupplier<double> tenacity)
        {
            _input = input;
            _length = length;
            _stepSize = stepSize;
            _tenacity = tenacity;
        }

        public Path Get()
        {
            var path = new Path(_input.Get());

            foreach (var segment in path.Leaves())
            {
                var length = _length.Get().WithMin(0);
                var stepSize = _stepSize.Get().WithMin(1);
                var tenacity = _tenacity.Get().InRange01();

                var extParams = segment.TraceParams;

                extParams.StepSize = stepSize;
                extParams.AngleTenacity = tenacity;

                segment.ExtendWithParams(extParams, length);
            }

            return path;
        }

        public void ResetState()
        {
            _input.ResetState();
            _length.ResetState();
            _stepSize.ResetState();
            _tenacity.ResetState();
        }
    }
}
