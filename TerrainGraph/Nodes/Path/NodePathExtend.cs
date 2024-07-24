using System;
using System.Linq;
using NodeEditorFramework;
using TerrainGraph.Flow;
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

    [ValueConnectionKnob("Step size", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob StepSizeKnob;

    [ValueConnectionKnob("Output", Direction.Out, PathFunctionConnection.Id)]
    public ValueConnectionKnob OutputKnob;

    public double Length = 100;
    public double StepSize = 5;

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

        GUILayout.EndVertical();

        if (GUI.changed)
            canvas.OnNodeChange(this);
    }

    public override void RefreshPreview()
    {
        var length = GetIfConnected<double>(LengthKnob);
        var stepSize = GetIfConnected<double>(StepSizeKnob);

        length?.ResetState();
        stepSize?.ResetState();

        if (length != null) Length = length.Get();
        if (stepSize != null) StepSize = stepSize.Get();
    }

    public override bool Calculate()
    {
        OutputKnob.SetValue<ISupplier<Path>>(new Output(
            SupplierOrFallback(InputKnob, Path.Empty),
            SupplierOrFallback(LengthKnob, Length),
            SupplierOrFallback(StepSizeKnob, StepSize)
        ));
        return true;
    }

    private class Output : ISupplier<Path>
    {
        private readonly ISupplier<Path> _input;
        private readonly ISupplier<double> _length;
        private readonly ISupplier<double> _stepSize;

        public Output(
            ISupplier<Path> input,
            ISupplier<double> length,
            ISupplier<double> stepSize)
        {
            _input = input;
            _length = length;
            _stepSize = stepSize;
        }

        public Path Get()
        {
            var path = new Path(_input.Get());

            foreach (var segment in path.Leaves.ToList())
            {
                var length = _length.Get().WithMin(0);
                var stepSize = _stepSize.Get().WithMin(1);

                var extParams = segment.TraceParams;

                extParams.StepSize = stepSize;
                extParams.Target = null;

                segment.ExtendWithParams(extParams, length);
            }

            return path;
        }

        public void ResetState()
        {
            _input.ResetState();
            _length.ResetState();
            _stepSize.ResetState();
        }
    }
}
