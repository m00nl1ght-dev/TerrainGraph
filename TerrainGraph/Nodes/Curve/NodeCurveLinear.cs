using System;
using NodeEditorFramework;
using NodeEditorFramework.Utilities;
using UnityEngine;

namespace TerrainGraph;

[Serializable]
[Node(false, "Curve/Linear Function", 190)]
public class NodeCurveLinear : NodeBase
{
    public const string ID = "curveLinear";
    public override string GetID => ID;

    public override string Title => "Linear Function";

    [NonSerialized]
    public ValueConnectionKnob BiasKnob;

    [NonSerialized]
    public ValueConnectionKnob ClampMinKnob;

    [NonSerialized]
    public ValueConnectionKnob ClampMaxKnob;

    [NonSerialized]
    public ValueConnectionKnob SlopeKnob;

    private static readonly ValueConnectionKnobAttribute BiasKnobAttribute = new("Bias", Direction.In, ValueFunctionConnection.Id);
    private static readonly ValueConnectionKnobAttribute ClampMinKnobAttribute = new("Clamp min", Direction.In, ValueFunctionConnection.Id);
    private static readonly ValueConnectionKnobAttribute ClampMaxKnobAttribute = new("Clamp max", Direction.In, ValueFunctionConnection.Id);
    private static readonly ValueConnectionKnobAttribute SlopeKnobAttribute = new("Slope", Direction.In, ValueFunctionConnection.Id);

    [ValueConnectionKnob("Output", Direction.Out, CurveFunctionConnection.Id)]
    public ValueConnectionKnob OutputKnob;

    public double Bias;
    public double ClampMin = double.MinValue;
    public double ClampMax = double.MaxValue;
    public double Slope = 1;

    public override void RefreshDynamicKnobs()
    {
        BiasKnob = FindDynamicKnob(BiasKnobAttribute);
        ClampMinKnob = FindDynamicKnob(ClampMinKnobAttribute);
        ClampMaxKnob = FindDynamicKnob(ClampMaxKnobAttribute);
        SlopeKnob = FindDynamicKnob(SlopeKnobAttribute);
    }

    public override void OnCreate(bool fromGUI)
    {
        base.OnCreate(fromGUI);
        if (fromGUI)
        {
            BiasKnob ??= (ValueConnectionKnob) CreateConnectionKnob(BiasKnobAttribute);
            SlopeKnob ??= (ValueConnectionKnob) CreateConnectionKnob(SlopeKnobAttribute);
        }
    }

    public override void NodeGUI()
    {
        OutputKnob.SetPosition(FirstKnobPosition);

        GUILayout.BeginVertical(BoxStyle);

        if (BiasKnob != null) KnobValueField(BiasKnob, ref Bias);
        if (ClampMinKnob != null) KnobValueField(ClampMinKnob, ref ClampMin);
        if (ClampMaxKnob != null) KnobValueField(ClampMaxKnob, ref ClampMax);
        if (SlopeKnob != null) KnobValueField(SlopeKnob, ref Slope);

        GUILayout.EndVertical();

        if (GUI.changed)
            canvas.OnNodeChange(this);
    }

    public override void RefreshPreview()
    {
        var bias = GetIfConnected<double>(BiasKnob);
        var min = GetIfConnected<double>(ClampMinKnob);
        var max = GetIfConnected<double>(ClampMaxKnob);
        var slope = GetIfConnected<double>(SlopeKnob);

        foreach (var supplier in new[] { bias, min, max, slope }) supplier?.ResetState();

        if (bias != null) Bias = bias.Get();
        if (min != null) ClampMin = min.Get();
        if (max != null) ClampMax = max.Get();
        if (slope != null) Slope = slope.Get();
    }

    public override void FillNodeActionsMenu(NodeEditorInputInfo inputInfo, GenericMenu menu)
    {
        base.FillNodeActionsMenu(inputInfo, menu);
        menu.AddSeparator("");

        if (ClampMinKnob != null)
        {
            menu.AddItem(new GUIContent("Remove clamp"), false, () =>
            {
                DeleteConnectionPort(ClampMinKnob);
                DeleteConnectionPort(ClampMaxKnob);
                RefreshDynamicKnobs();
                ClampMin = double.MinValue;
                ClampMax = double.MaxValue;
                canvas.OnNodeChange(this);
            });
        }
        else
        {
            menu.AddItem(new GUIContent("Add clamp"), false, () =>
            {
                ClampMinKnob ??= (ValueConnectionKnob) CreateConnectionKnob(ClampMinKnobAttribute);
                ClampMaxKnob ??= (ValueConnectionKnob) CreateConnectionKnob(ClampMaxKnobAttribute);
                ClampMin = -1;
                ClampMax = 1;
                canvas.OnNodeChange(this);
            });
        }
    }

    public override bool Calculate()
    {
        OutputKnob.SetValue<ISupplier<ICurveFunction<double>>>(new Output(
            SupplierOrFallback(BiasKnob, Bias),
            SupplierOrFallback(ClampMinKnob, ClampMin),
            SupplierOrFallback(ClampMaxKnob, ClampMax),
            SupplierOrFallback(SlopeKnob, Slope)
        ));
        return true;
    }

    private class Output : ISupplier<ICurveFunction<double>>
    {
        private readonly ISupplier<double> _bias;
        private readonly ISupplier<double> _clampMin;
        private readonly ISupplier<double> _clampMax;
        private readonly ISupplier<double> _slope;

        public Output(
            ISupplier<double> bias,
            ISupplier<double> clampMin,
            ISupplier<double> clampMax,
            ISupplier<double> slope)
        {
            _bias = bias;
            _clampMin = clampMin;
            _clampMax = clampMax;
            _slope = slope;
        }

        public ICurveFunction<double> Get()
        {
            return new CurveFunction.Clamp(
                new CurveFunction.SpanFunction(_bias.Get(), _slope.Get()),
                _clampMin.Get(), _clampMax.Get()
            );
        }

        public void ResetState()
        {
            _bias.ResetState();
            _clampMin.ResetState();
            _clampMax.ResetState();
            _slope.ResetState();
        }
    }
}
