using System;
using NodeEditorFramework;
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
    
    [ValueConnectionKnob("Width Loss", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob WidthLossKnob;

    [ValueConnectionKnob("Output", Direction.Out, PathFunctionConnection.Id)]
    public ValueConnectionKnob OutputKnob;

    public double Length = 1;
    public double WidthLoss;

    public override void NodeGUI()
    {
        InputKnob.SetPosition(FirstKnobPosition);
        OutputKnob.SetPosition(FirstKnobPosition);

        GUILayout.BeginVertical(BoxStyle);
        
        GUILayout.BeginHorizontal(BoxStyle);
        GUILayout.Label("Input", BoxLayout);
        GUILayout.EndHorizontal();

        KnobValueField(LengthKnob, ref Length);
        KnobValueField(WidthLossKnob, ref WidthLoss);

        GUILayout.EndVertical();

        if (GUI.changed)
            canvas.OnNodeChange(this);
    }

    public override void RefreshPreview()
    {
        var length = GetIfConnected<double>(LengthKnob);
        var widthLoss = GetIfConnected<double>(WidthLossKnob);
        
        length?.ResetState();
        widthLoss?.ResetState();
        
        if (length != null) Length = length.Get();
        if (widthLoss != null) WidthLoss = widthLoss.Get();
    }

    public override bool Calculate()
    {
        OutputKnob.SetValue<ISupplier<Path>>(new Output(
            SupplierOrFixed(InputKnob, Path.Empty),
            SupplierOrValueFixed(LengthKnob, Length),
            SupplierOrValueFixed(WidthLossKnob, WidthLoss)
        ));
        return true;
    }

    private class Output : ISupplier<Path>
    {
        private readonly ISupplier<Path> _input;
        private readonly ISupplier<double> _length;
        private readonly ISupplier<double> _widthLoss;

        public Output(ISupplier<Path> input, ISupplier<double> length, ISupplier<double> widthLoss)
        {
            _input = input;
            _length = length;
            _widthLoss = widthLoss;
        }

        public Path Get()
        {
            var path = new Path(_input.Get());
            
            foreach (var segment in path.Leaves())
            {
                var length = _length.Get();
                
                var extParams = segment.ExtendParams;
                extParams.WidthLoss = _widthLoss.Get();

                segment.ExtendWithParams(extParams, length);
            }
            
            return path;
        }

        public void ResetState()
        {
            _input.ResetState();
            _length.ResetState();
            _widthLoss.ResetState();
        }
    }
}
