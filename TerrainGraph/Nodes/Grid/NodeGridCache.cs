using System;
using NodeEditorFramework;

namespace TerrainGraph;

[Serializable]
[Node(false, "Grid/Cache", 225)]
public class NodeGridCache : NodeCacheBase
{
    public const string ID = "gridCache";
    public override string GetID => ID;

    [ValueConnectionKnob("Input", Direction.In, GridFunctionConnection.Id)]
    public ValueConnectionKnob InputKnob;

    [ValueConnectionKnob("Output", Direction.Out, GridFunctionConnection.Id)]
    public ValueConnectionKnob OutputKnob;

    public override ValueConnectionKnob InputKnobRef => InputKnob;
    public override ValueConnectionKnob OutputKnobRef => OutputKnob;

    public override bool Calculate()
    {
        OutputKnob.SetValue<ISupplier<IGridFunction<double>>>(new Output<IGridFunction<double>>(
            SupplierOrGridFixed(InputKnob, GridFunction.Zero)
        ));
        return true;
    }
}
