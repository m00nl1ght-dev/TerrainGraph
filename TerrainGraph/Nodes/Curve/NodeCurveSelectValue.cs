using System;
using System.Collections.Generic;
using NodeEditorFramework;
using TerrainGraph.Util;

namespace TerrainGraph;

[Serializable]
[Node(false, "Curve/Select/Value", 170)]
public class NodeCurveSelectValue : NodeSelectBase<double, double>
{
    public const string ID = "curveSelectValue";
    public override string GetID => ID;

    [ValueConnectionKnob("Input", Direction.In, CurveFunctionConnection.Id)]
    public ValueConnectionKnob InputKnob;

    [ValueConnectionKnob("Output", Direction.Out, CurveFunctionConnection.Id)]
    public ValueConnectionKnob OutputKnob;

    public override ValueConnectionKnob InputKnobRef => InputKnob;
    public override ValueConnectionKnob OutputKnobRef => OutputKnob;

    protected override string OptionConnectionTypeId => CurveFunctionConnection.Id;

    public override bool SupportsInterpolation => true;

    protected override void DrawOptionKey(int i) => DrawDoubleOptionKey(Thresholds, i);

    protected override void DrawOptionValue(int i) => DrawDoubleOptionValue(Values, i);

    public override bool Calculate()
    {
        var input = SupplierOrFallback(InputKnob, CurveFunction.Zero);

        List<ISupplier<ICurveFunction<double>>> options = [];

        for (int i = 0; i < Math.Min(Values.Count, OptionKnobs.Count); i++)
        {
            options.Add(SupplierOrFallback(OptionKnobs[i], CurveFunction.Of(Values[i])));
        }

        OutputKnob.SetValue<ISupplier<ICurveFunction<double>>>(
            new CurveOutput<double>(
                input, options, Thresholds,
                Interpolated ? MathUtil.Lerp : null
            )
        );

        return true;
    }
}
