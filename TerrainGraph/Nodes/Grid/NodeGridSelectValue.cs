using System;
using System.Collections.Generic;
using System.Linq;
using NodeEditorFramework;
using TerrainGraph.Util;

namespace TerrainGraph;

[Serializable]
[Node(false, "Grid/Select/Value", 200)]
public class NodeGridSelectValue : NodeSelectBase<double, double>
{
    public const string ID = "gridSelectValue";
    public override string GetID => ID;

    [ValueConnectionKnob("Input", Direction.In, GridFunctionConnection.Id)]
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
        var input = SupplierOrFallback(InputKnob, GridFunction.Zero);

        List<ISupplier<IGridFunction<double>>> options = [];

        for (int i = 0; i < Math.Min(Values.Count, OptionKnobs.Count); i++)
        {
            options.Add(SupplierOrFallback(OptionKnobs[i], GridFunction.Of(Values[i])));
        }

        OutputKnob.SetValue<ISupplier<IGridFunction<double>>>(
            new GridOutput<double>(
                input, options, Thresholds.ToList(),
                Interpolated ? MathUtil.Lerp : null
            )
        );

        return true;
    }
}
