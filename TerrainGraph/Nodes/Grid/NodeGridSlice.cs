using System;
using NodeEditorFramework;
using UnityEngine;

namespace TerrainGraph;

[Serializable]
[Node(false, "Grid/Slice", 216)]
public class NodeGridSlice : NodeBase
{
    public const string ID = "gridSlice";
    public override string GetID => ID;

    public override string Title => "Grid Slice";

    [ValueConnectionKnob("Input", Direction.In, GridFunctionConnection.Id)]
    public ValueConnectionKnob InputKnob;

    [ValueConnectionKnob("Position", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob PositionKnob;

    [ValueConnectionKnob("Output", Direction.Out, CurveFunctionConnection.Id)]
    public ValueConnectionKnob OutputKnob;

    public double Position;

    public override void NodeGUI()
    {
        InputKnob.SetPosition(FirstKnobPosition);
        OutputKnob.SetPosition(FirstKnobPosition);

        GUILayout.BeginVertical(BoxStyle);

        GUILayout.BeginHorizontal(BoxStyle);
        GUILayout.Label("Input", BoxLayout);
        GUILayout.EndHorizontal();

        KnobValueField(PositionKnob, ref Position);

        GUILayout.EndVertical();

        if (GUI.changed)
            canvas.OnNodeChange(this);
    }

    public override void RefreshPreview()
    {
        var pos = GetIfConnected<double>(PositionKnob);

        pos?.ResetState();

        if (pos != null) Position = pos.Get();
    }

    public override void CleanUpGUI()
    {
        if (PositionKnob.connected()) Position = 0;
    }

    public override bool Calculate()
    {
        OutputKnob.SetValue<ISupplier<ICurveFunction<double>>>(new Output(
            SupplierOrFallback(InputKnob, GridFunction.Zero),
            SupplierOrFallback(PositionKnob, Position)
        ));
        return true;
    }

    public class Output : ISupplier<ICurveFunction<double>>
    {
        private readonly ISupplier<IGridFunction<double>> _input;
        private readonly ISupplier<double> _position;

        public Output(ISupplier<IGridFunction<double>> input, ISupplier<double> position)
        {
            _input = input;
            _position = position;
        }

        public ICurveFunction<double> Get()
        {
            return new CurveFunction.GridSlice<double>(_input.Get(), _position.Get());
        }

        public void ResetState()
        {
            _input.ResetState();
            _position.ResetState();
        }
    }
}
