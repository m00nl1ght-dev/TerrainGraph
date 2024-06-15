using System;
using NodeEditorFramework;
using NodeEditorFramework.Utilities;
using UnityEngine;

namespace TerrainGraph;

[Serializable]
[Node(false, "Curve/Const", 170)]
public class NodeCurveFromValue : NodeBase
{
    public const string ID = "curveFromValue";
    public override string GetID => ID;

    public override Vector2 DefaultSize => new(100, 55);
    public override bool AutoLayout => false;

    public override string Title => "Curve";

    [ValueConnectionKnob("Input", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob InputKnob;

    [ValueConnectionKnob("Output", Direction.Out, CurveFunctionConnection.Id)]
    public ValueConnectionKnob OutputKnob;

    public double Value;

    public override void NodeGUI()
    {
        InputKnob.SetPosition(FirstKnobPosition);
        OutputKnob.SetPosition(FirstKnobPosition);

        GUILayout.BeginVertical(BoxStyle);
        GUILayout.BeginHorizontal(BoxStyle);

        if (InputKnob.connected())
        {
            GUI.enabled = false;
            RTEditorGUI.FloatField(GUIContent.none, (float) Math.Round(Value, 2), FullBoxLayout);
            GUI.enabled = true;
        }
        else
        {
            Value = RTEditorGUI.FloatField(GUIContent.none, (float) Value, FullBoxLayout);
        }

        GUILayout.EndHorizontal();
        GUILayout.EndVertical();

        if (GUI.changed)
            canvas.OnNodeChange(this);
    }

    public override void RefreshPreview()
    {
        var input = GetIfConnected<double>(InputKnob);

        input?.ResetState();

        if (input != null) Value = input.Get();
    }

    public override bool Calculate()
    {
        OutputKnob.SetValue<ISupplier<ICurveFunction<double>>>(new Output<double>(
            SupplierOrFallback(InputKnob, Value)
        ));
        return true;
    }

    public class Output<T> : ISupplier<ICurveFunction<T>>
    {
        private readonly ISupplier<T> _input;

        public Output(ISupplier<T> input)
        {
            _input = input;
        }

        public ICurveFunction<T> Get()
        {
            return CurveFunction.Of(_input.Get());
        }

        public void ResetState()
        {
            _input.ResetState();
        }
    }
}
