using System;
using NodeEditorFramework;
using UnityEngine;

namespace TerrainGraph;

[Serializable]
[Node(false, "Curve/Transform", 182)]
public class NodeCurveTransform : NodeBase
{
    public const string ID = "curveTransform";
    public override string GetID => ID;

    public override string Title => "Transform";

    [ValueConnectionKnob("Input", Direction.In, CurveFunctionConnection.Id)]
    public ValueConnectionKnob InputKnob;

    [ValueConnectionKnob("Displace", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob DisplaceKnob;

    [ValueConnectionKnob("Scale", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob ScaleKnob;

    [ValueConnectionKnob("Output", Direction.Out, CurveFunctionConnection.Id)]
    public ValueConnectionKnob OutputKnob;

    public double Displace;
    public double Scale = 1;

    public override void NodeGUI()
    {
        InputKnob.SetPosition(FirstKnobPosition);
        OutputKnob.SetPosition(FirstKnobPosition);

        GUILayout.BeginVertical(BoxStyle);

        GUILayout.BeginHorizontal(BoxStyle);
        GUILayout.Label("Input", BoxLayout);
        GUILayout.EndHorizontal();

        KnobValueField(DisplaceKnob, ref Displace);
        KnobValueField(ScaleKnob, ref Scale);

        GUILayout.EndVertical();

        if (GUI.changed)
            canvas.OnNodeChange(this);
    }

    public override void RefreshPreview()
    {
        var displace = GetIfConnected<double>(DisplaceKnob);
        var scale = GetIfConnected<double>(ScaleKnob);

        displace?.ResetState();
        scale?.ResetState();

        if (displace != null) Displace = displace.Get();
        if (scale != null) Scale = scale.Get();
    }

    public override void CleanUpGUI()
    {
        if (DisplaceKnob.connected()) Displace = 0;
        if (ScaleKnob.connected()) Scale = 0;
    }

    public override bool Calculate()
    {
        OutputKnob.SetValue<ISupplier<ICurveFunction<double>>>(new Output(
            SupplierOrFallback(InputKnob, CurveFunction.Zero),
            SupplierOrFallback(DisplaceKnob, Displace),
            SupplierOrFallback(ScaleKnob, Scale)
        ));
        return true;
    }

    public class Output : ISupplier<ICurveFunction<double>>
    {
        private readonly ISupplier<ICurveFunction<double>> _input;
        private readonly ISupplier<double> _displace;
        private readonly ISupplier<double> _scale;

        public Output(
            ISupplier<ICurveFunction<double>> input,
            ISupplier<double> displace,
            ISupplier<double> scale)
        {
            _input = input;
            _displace = displace;
            _scale = scale;
        }

        public ICurveFunction<double> Get()
        {
            return new CurveFunction.Transform<double>(
                _input.Get(), _displace.Get(), _scale.Get()
            );
        }

        public void ResetState()
        {
            _input.ResetState();
            _displace.ResetState();
            _scale.ResetState();
        }
    }
}
