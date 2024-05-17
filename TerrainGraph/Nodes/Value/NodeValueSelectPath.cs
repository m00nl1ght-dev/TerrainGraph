using System;
using System.Linq;
using NodeEditorFramework;
using TerrainGraph.Flow;
using UnityEngine;

namespace TerrainGraph;

[Serializable]
[Node(false, "Value/Select/Path", 102)]
public class NodeValueSelectPath : NodeSelectBase<double, byte>
{
    public const string ID = "valueSelectPath";
    public override string GetID => ID;

    [ValueConnectionKnob("Input", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob InputKnob;

    [ValueConnectionKnob("Output", Direction.Out, PathFunctionConnection.Id)]
    public ValueConnectionKnob OutputKnob;

    public override ValueConnectionKnob InputKnobRef => InputKnob;
    public override ValueConnectionKnob OutputKnobRef => OutputKnob;

    protected override string OptionConnectionTypeId => PathFunctionConnection.Id;

    protected override void DrawOptionKey(int i) => DrawDoubleOptionKey(Thresholds, i);

    protected override void DrawOptionValue(int i)
    {
        GUILayout.Label("Option " + (i + 1), BoxLayout);
    }

    public override bool Calculate()
    {
        var input = SupplierOrFallback(InputKnob, 0d);

        var options = OptionKnobs.Select(option => SupplierOrFallback(option, Path.Empty)).ToList();

        OutputKnob.SetValue<ISupplier<Path>>(
            new Output<Path>(
                input, options, Thresholds, null
            )
        );

        return true;
    }
}
