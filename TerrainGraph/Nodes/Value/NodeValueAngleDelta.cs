using System;
using NodeEditorFramework;
using TerrainGraph.Util;
using UnityEngine;

namespace TerrainGraph;

[Serializable]
[Node(false, "Value/Angle Delta", 116)]
public class NodeValueAngleDelta : NodeBase
{
    public const string ID = "valueAngleDelta";
    public override string GetID => ID;

    public override string Title => "Angle Delta";

    [ValueConnectionKnob("First", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob FirstKnob;

    [ValueConnectionKnob("Second", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob SecondKnob;

    [ValueConnectionKnob("Output", Direction.Out, ValueFunctionConnection.Id)]
    public ValueConnectionKnob OutputKnob;

    public double First;
    public double Second;

    public override void NodeGUI()
    {
        OutputKnob.SetPosition(FirstKnobPosition);

        GUILayout.BeginVertical(BoxStyle);

        KnobValueField(FirstKnob, ref First);
        KnobValueField(SecondKnob, ref Second);

        GUILayout.EndVertical();

        if (GUI.changed)
            canvas.OnNodeChange(this);
    }

    public override void RefreshPreview()
    {
        var first = GetIfConnected<double>(FirstKnob);
        var second = GetIfConnected<double>(SecondKnob);

        first?.ResetState();
        second?.ResetState();

        if (first != null) First = first.Get();
        if (second != null) Second = second.Get();
    }

    public override bool Calculate()
    {
        OutputKnob.SetValue<ISupplier<double>>(new Output(
            SupplierOrFallback(FirstKnob, First),
            SupplierOrFallback(SecondKnob, Second)
        ));
        return true;
    }

    private class Output : ISupplier<double>
    {
        private readonly ISupplier<double> _first;
        private readonly ISupplier<double> _second;

        public Output(ISupplier<double> first, ISupplier<double> second)
        {
            _first = first;
            _second = second;
        }

        public double Get()
        {
            return MathUtil.AngleDelta(_first.Get(), _second.Get());
        }

        public void ResetState()
        {
            _first.ResetState();
            _second.ResetState();
        }
    }
}
