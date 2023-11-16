using System;
using NodeEditorFramework;
using UnityEngine;

namespace TerrainGraph;

[Serializable]
[Node(false, "Path/Avoid", 604)]
public class NodePathAvoid : NodeBase
{
    public const string ID = "pathAvoid";
    public override string GetID => ID;

    public override string Title => "Path: Avoid";
    
    [ValueConnectionKnob("Input", Direction.In, PathFunctionConnection.Id)]
    public ValueConnectionKnob InputKnob;

    [ValueConnectionKnob("Avoid Grid", Direction.In, GridFunctionConnection.Id)]
    public ValueConnectionKnob AvoidGridKnob;
    
    [ValueConnectionKnob("Strength", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob StrengthKnob;

    [ValueConnectionKnob("Output", Direction.Out, PathFunctionConnection.Id)]
    public ValueConnectionKnob OutputKnob;

    public double Strength = 1;

    public override void NodeGUI()
    {
        InputKnob.SetPosition(FirstKnobPosition);
        OutputKnob.SetPosition(FirstKnobPosition);

        GUILayout.BeginVertical(BoxStyle);
        
        GUILayout.BeginHorizontal(BoxStyle);
        GUILayout.Label("Input", BoxLayout);
        GUILayout.EndHorizontal();
        
        GUILayout.BeginHorizontal(BoxStyle);
        GUILayout.Label("Avoid Grid", BoxLayout);
        GUILayout.EndHorizontal();
        
        AvoidGridKnob.SetPosition();
        
        KnobValueField(StrengthKnob, ref Strength);

        GUILayout.EndVertical();

        if (GUI.changed)
            canvas.OnNodeChange(this);
    }

    public override void RefreshPreview()
    {
        var strength = GetIfConnected<double>(StrengthKnob);

        strength?.ResetState();
        
        if (strength != null) Strength = strength.Get();
    }

    public override bool Calculate()
    {
        OutputKnob.SetValue<ISupplier<Path>>(new Output(
            SupplierOrFixed(InputKnob, Path.Empty),
            SupplierOrGridFixed(AvoidGridKnob, GridFunction.Zero),
            SupplierOrValueFixed(StrengthKnob, Strength)
        ));
        return true;
    }

    private class Output : ISupplier<Path>
    {
        private readonly ISupplier<Path> _input;
        private readonly ISupplier<IGridFunction<double>> _avoidGrid;
        private readonly ISupplier<double> _strength;

        public Output(ISupplier<Path> input, ISupplier<IGridFunction<double>> avoidGrid, ISupplier<double> strength)
        {
            _input = input;
            _avoidGrid = avoidGrid;
            _strength = strength;
        }

        public Path Get()
        {
            var path = new Path(_input.Get());
            
            foreach (var segment in path.Leaves())
            {
                var extParams = segment.ExtendParams;
                
                extParams.AvoidGrid = _avoidGrid.Get();
                extParams.AvoidStrength = _strength.Get();

                segment.ExtendWithParams(extParams);
            }
            
            return path;
        }

        public void ResetState()
        {
            _input.ResetState();
            _avoidGrid.ResetState();
            _strength.ResetState();
        }
    }
}
