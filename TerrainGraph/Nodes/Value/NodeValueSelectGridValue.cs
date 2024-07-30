using System;
using System.Collections.Generic;
using System.Linq;
using NodeEditorFramework;
using static TerrainGraph.GridFunction;

namespace TerrainGraph;

[Serializable]
[Node(false, "Value/Select/Grid", 101)]
public class NodeValueSelectGridValue : NodeSelectBase<double, double>
{
    public const string ID = "valueSelectGridValue";
    public override string GetID => ID;

    [ValueConnectionKnob("Input", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob InputKnob;

    [ValueConnectionKnob("Output", Direction.Out, GridFunctionConnection.Id)]
    public ValueConnectionKnob OutputKnob;

    public override ValueConnectionKnob InputKnobRef => InputKnob;
    public override ValueConnectionKnob OutputKnobRef => OutputKnob;

    protected override string OptionConnectionTypeId => GridFunctionConnection.Id;

    public override bool SupportsInterpolation => true;

    protected override void DrawOptionKey(int i) => DrawDoubleOptionKey(Thresholds, i);

    protected override void DrawOptionValue(int i) => DrawDoubleOptionValue(Values, i);

    public override bool Calculate()
    {
        var input = SupplierOrFallback(InputKnob, 0d);

        List<ISupplier<IGridFunction<double>>> options = [];

        for (int i = 0; i < Math.Min(Values.Count, OptionKnobs.Count); i++)
        {
            options.Add(SupplierOrFallback(OptionKnobs[i], Of(Values[i])));
        }

        OutputKnob.SetValue<ISupplier<IGridFunction<double>>>(
            new Output<IGridFunction<double>>(
                input, options, Thresholds.ToList(),
                Interpolated ? (t, a, b) => Lerp.Of(a, b, t) : null
            )
        );

        return true;
    }
}
