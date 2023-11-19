using System;
using NodeEditorFramework;
using TerrainGraph.Util;
using UnityEngine;

namespace TerrainGraph;

[Serializable]
[Node(false, "Path/Extend", 602)]
public class NodePathExtend : NodeBase
{
    public const string ID = "pathExtend";
    public override string GetID => ID;

    public override string Title => "Path: Extend";

    [ValueConnectionKnob("Input", Direction.In, PathFunctionConnection.Id)]
    public ValueConnectionKnob InputKnob;

    [ValueConnectionKnob("Length", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob LengthKnob;

    [ValueConnectionKnob("Output", Direction.Out, PathFunctionConnection.Id)]
    public ValueConnectionKnob OutputKnob;

    public double Length = 1;

    public override void NodeGUI()
    {
        InputKnob.SetPosition(FirstKnobPosition);
        OutputKnob.SetPosition(FirstKnobPosition);

        GUILayout.BeginVertical(BoxStyle);

        GUILayout.BeginHorizontal(BoxStyle);
        GUILayout.Label("Input", BoxLayout);
        GUILayout.EndHorizontal();

        KnobValueField(LengthKnob, ref Length);

        GUILayout.EndVertical();

        if (GUI.changed)
            canvas.OnNodeChange(this);
    }

    public override void RefreshPreview()
    {
        var length = GetIfConnected<double>(LengthKnob);

        length?.ResetState();

        if (length != null) Length = length.Get();
    }

    public override bool Calculate()
    {
        OutputKnob.SetValue<ISupplier<Path>>(new Output(
            SupplierOrFixed(InputKnob, Path.Empty),
            SupplierOrValueFixed(LengthKnob, Length)
        ));
        return true;
    }

    private class Output : ISupplier<Path>
    {
        private readonly ISupplier<Path> _input;
        private readonly ISupplier<double> _length;

        public Output(ISupplier<Path> input, ISupplier<double> length)
        {
            _input = input;
            _length = length;
        }

        public Path Get()
        {
            var path = new Path(_input.Get());

            foreach (var segment in path.Leaves())
            {
                segment.Length += _length.Get().WithMin(0);
            }

            return path;
        }

        public void ResetState()
        {
            _input.ResetState();
            _length.ResetState();
        }
    }
}
