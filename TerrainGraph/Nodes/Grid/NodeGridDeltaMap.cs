using System;
using NodeEditorFramework;
using UnityEngine;

namespace TerrainGraph;

[Serializable]
[Node(false, "Grid/Delta Map", 214)]
public class NodeGridDeltaMap : NodeBase
{
    public const string ID = "gridDeltaMap";
    public override string GetID => ID;

    public override string Title => "Delta Map";

    [ValueConnectionKnob("Input", Direction.In, GridFunctionConnection.Id)]
    public ValueConnectionKnob InputKnob;

    [ValueConnectionKnob("Step", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob StepKnob;

    [ValueConnectionKnob("Output", Direction.Out, GridFunctionConnection.Id)]
    public ValueConnectionKnob OutputKnob;

    public double Step = 1;

    public override void NodeGUI()
    {
        InputKnob.SetPosition(FirstKnobPosition);
        OutputKnob.SetPosition(FirstKnobPosition);

        GUILayout.BeginVertical(BoxStyle);

        GUILayout.BeginHorizontal(BoxStyle);
        GUILayout.Label("Input", BoxLayout);
        GUILayout.EndHorizontal();

        KnobValueField(StepKnob, ref Step);

        GUILayout.EndVertical();

        if (GUI.changed)
            canvas.OnNodeChange(this);
    }

    public override void RefreshPreview()
    {
        var supplier = GetIfConnected<double>(StepKnob);
        if (supplier != null) Step = supplier.ResetAndGet();
    }

    public override bool Calculate()
    {
        OutputKnob.SetValue<ISupplier<IGridFunction<double>>>(new Output(
            SupplierOrGridFixed(InputKnob, GridFunction.Zero),
            SupplierOrValueFixed(StepKnob, Step)
        ));
        return true;
    }

    public class Output : ISupplier<IGridFunction<double>>
    {
        private readonly ISupplier<IGridFunction<double>> _input;
        private readonly ISupplier<double> _step;

        public Output(ISupplier<IGridFunction<double>> input, ISupplier<double> step)
        {
            _input = input;
            _step = step;
        }

        public IGridFunction<double> Get()
        {
            return new GridFunction.DeltaMap(_input.Get(), _step.Get());
        }

        public void ResetState()
        {
            _input.ResetState();
            _step.ResetState();
        }
    }
}
