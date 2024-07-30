using System;
using System.Collections.Generic;
using System.Linq;
using NodeEditorFramework;
using TerrainGraph.Util;

namespace TerrainGraph;

[Serializable]
[Node(false, "Value/Select/Value", 100)]
public class NodeValueSelectValue : NodeSelectBase<double, double>
{
    public const string ID = "valueSelectValue";
    public override string GetID => ID;

    [ValueConnectionKnob("Input", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob InputKnob;

    [ValueConnectionKnob("Output", Direction.Out, ValueFunctionConnection.Id)]
    public ValueConnectionKnob OutputKnob;

    public override ValueConnectionKnob InputKnobRef => InputKnob;
    public override ValueConnectionKnob OutputKnobRef => OutputKnob;

    protected override string OptionConnectionTypeId => ValueFunctionConnection.Id;

    public override bool SupportsInterpolation => true;

    public override void RefreshPreview() => RefreshPreview(MathUtil.Identity);

    protected override void DrawOptionKey(int i) => DrawDoubleOptionKey(Thresholds, i);

    protected override void DrawOptionValue(int i) => DrawDoubleOptionValue(Values, i, true);

    public override bool Calculate()
    {
        var input = SupplierOrFallback(InputKnob, 0d);

        List<ISupplier<double>> options = [];

        for (int i = 0; i < Math.Min(Values.Count, OptionKnobs.Count); i++)
        {
            options.Add(SupplierOrFallback(OptionKnobs[i], Values[i]));
        }

        OutputKnob.SetValue<ISupplier<double>>(
            new Output<double>(
                input, options, Thresholds.ToList(),
                Interpolated ? MathUtil.Lerp : null
            )
        );

        return true;
    }
}
