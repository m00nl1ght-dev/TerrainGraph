using System;
using NodeEditorFramework;
using UnityEngine;

namespace TerrainGraph;

[Serializable]
[Node(false, "Grid/Apply Curve", 216)]
public class NodeGridApplyCurve : NodeBase
{
    public const string ID = "gridApplyCurve";
    public override string GetID => ID;

    public override string Title => "Apply Curve";

    [ValueConnectionKnob("Input", Direction.In, GridFunctionConnection.Id)]
    public ValueConnectionKnob InputKnob;

    [ValueConnectionKnob("Curve", Direction.In, CurveFunctionConnection.Id)]
    public ValueConnectionKnob CurveKnob;

    [ValueConnectionKnob("Output", Direction.Out, GridFunctionConnection.Id)]
    public ValueConnectionKnob OutputKnob;

    public override void NodeGUI()
    {
        InputKnob.SetPosition(FirstKnobPosition);
        OutputKnob.SetPosition(FirstKnobPosition);

        GUILayout.BeginVertical(BoxStyle);

        GUILayout.BeginHorizontal(BoxStyle);
        GUILayout.Label("Input", BoxLayout);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal(BoxStyle);
        GUILayout.Label("Curve", BoxLayout);
        GUILayout.EndHorizontal();

        CurveKnob.SetPosition();

        GUILayout.EndVertical();

        if (GUI.changed)
            canvas.OnNodeChange(this);
    }

    public override bool Calculate()
    {
        OutputKnob.SetValue<ISupplier<IGridFunction<double>>>(new Output(
            SupplierOrFallback(InputKnob, GridFunction.Zero),
            SupplierOrFallback(CurveKnob, CurveFunction.One)
        ));
        return true;
    }

    public class Output : ISupplier<IGridFunction<double>>
    {
        private readonly ISupplier<IGridFunction<double>> _input;
        private readonly ISupplier<ICurveFunction<double>> _curve;

        public Output(ISupplier<IGridFunction<double>> input, ISupplier<ICurveFunction<double>> curve)
        {
            _input = input;
            _curve = curve;
        }

        public IGridFunction<double> Get()
        {
            return new GridFunction.ApplyCurve<double>(_input.Get(), _curve.Get());
        }

        public void ResetState()
        {
            _input.ResetState();
            _curve.ResetState();
        }
    }
}
