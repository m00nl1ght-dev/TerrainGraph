using System;
using NodeEditorFramework;

namespace TerrainGraph;

[Serializable]
[Node(false, "Value/Cache", 125)]
public class NodeValueCache : NodeCacheBase
{
    public const string ID = "valueCache";
    public override string GetID => ID;

    [ValueConnectionKnob("Input", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob InputKnob;

    [ValueConnectionKnob("Output", Direction.Out, ValueFunctionConnection.Id)]
    public ValueConnectionKnob OutputKnob;

    public override ValueConnectionKnob InputKnobRef => InputKnob;
    public override ValueConnectionKnob OutputKnobRef => OutputKnob;

    public override bool Calculate()
    {
        OutputKnob.SetValue<ISupplier<double>>(new Output<double>(
            SupplierOrValueFixed(InputKnob, 0)
        ));
        return true;
    }
}
