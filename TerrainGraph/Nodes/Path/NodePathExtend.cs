using System;
using NodeEditorFramework;
using TerrainGraph.Util;
using UnityEngine;

namespace TerrainGraph;

[Serializable]
[Node(false, "Path/Extend", 601)]
public class NodePathExtend : NodeBase
{
    public const string ID = "pathExtend";
    public override string GetID => ID;

    public override string Title => "Path: Extend";

    [ValueConnectionKnob("Input", Direction.In, PathFunctionConnection.Id)]
    public ValueConnectionKnob InputKnob;

    [ValueConnectionKnob("Length", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob LengthKnob;

    [ValueConnectionKnob("Tenacity", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob TenacityKnob;

    [ValueConnectionKnob("Output", Direction.Out, PathFunctionConnection.Id)]
    public ValueConnectionKnob OutputKnob;

    public double Length = 1;
    public double Tenacity = 0.2;

    public override void NodeGUI()
    {
        InputKnob.SetPosition(FirstKnobPosition);
        OutputKnob.SetPosition(FirstKnobPosition);

        GUILayout.BeginVertical(BoxStyle);

        GUILayout.BeginHorizontal(BoxStyle);
        GUILayout.Label("Input", BoxLayout);
        GUILayout.EndHorizontal();

        KnobValueField(LengthKnob, ref Length);
        KnobValueField(TenacityKnob, ref Tenacity);

        GUILayout.EndVertical();

        if (GUI.changed)
            canvas.OnNodeChange(this);
    }

    public override void RefreshPreview()
    {
        var length = GetIfConnected<double>(LengthKnob);
        var tenacity = GetIfConnected<double>(TenacityKnob);

        length?.ResetState();
        tenacity?.ResetState();

        if (length != null) Length = length.Get();
        if (tenacity != null) Tenacity = tenacity.Get();
    }

    public override bool Calculate()
    {
        OutputKnob.SetValue<ISupplier<Path>>(new Output(
            SupplierOrFixed(InputKnob, Path.Empty),
            SupplierOrValueFixed(LengthKnob, Length),
            SupplierOrValueFixed(TenacityKnob, Tenacity)
        ));
        return true;
    }

    private class Output : ISupplier<Path>
    {
        private readonly ISupplier<Path> _input;
        private readonly ISupplier<double> _length;
        private readonly ISupplier<double> _tenacity;

        public Output(ISupplier<Path> input, ISupplier<double> length, ISupplier<double> tenacity)
        {
            _input = input;
            _length = length;
            _tenacity = tenacity;
        }

        public Path Get()
        {
            var path = new Path(_input.Get());

            foreach (var segment in path.Leaves())
            {
                var length = _length.Get().WithMin(0);
                var tenacity = _tenacity.Get().InRange01();

                var extParams = segment.ExtendParams;

                extParams.MaxTurnRate = 1 - tenacity;

                segment.ExtendWithParams(extParams, length);
            }

            return path;
        }

        public void ResetState()
        {
            _input.ResetState();
            _length.ResetState();
            _tenacity.ResetState();
        }
    }
}
