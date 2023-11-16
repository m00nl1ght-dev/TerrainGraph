using System;
using System.Collections.Generic;
using System.Linq;
using NodeEditorFramework;
using NodeEditorFramework.Utilities;
using UnityEngine;

namespace TerrainGraph;

[Serializable]
[Node(false, "Path/Split", 605)]
public class NodePathSplit : NodeBase
{
    public const string ID = "pathSplit";
    public override string GetID => ID;

    public override string Title => "Path: Split";
    
    [ValueConnectionKnob("Input", Direction.In, PathFunctionConnection.Id)]
    public ValueConnectionKnob InputKnob;
    
    [NonSerialized]
    public List<ValueConnectionKnob> AngleKnobs = new();
    
    [NonSerialized]
    public List<ValueConnectionKnob> WidthKnobs = new();
    
    [NonSerialized]
    public List<ValueConnectionKnob> OutputKnobs = new();
    
    public List<double> Angles = new();
    public List<double> Widths = new();
    
    public override void RefreshDynamicKnobs()
    {
        AngleKnobs = dynamicConnectionPorts.Where(k => k.name.StartsWith("Angle")).Cast<ValueConnectionKnob>().ToList();
        WidthKnobs = dynamicConnectionPorts.Where(k => k.name.StartsWith("Width")).Cast<ValueConnectionKnob>().ToList();
        OutputKnobs = dynamicConnectionPorts.Where(k => k.name.StartsWith("Output")).Cast<ValueConnectionKnob>().ToList();
    }

    public override void NodeGUI()
    {
        InputKnob.SetPosition(FirstKnobPosition);
        
        while (OutputKnobs.Count < 2) AddBranch();

        GUILayout.BeginVertical(BoxStyle);
        
        GUILayout.BeginHorizontal(BoxStyle);
        GUILayout.Label("Input", BoxLayout);
        GUILayout.EndHorizontal();

        for (int i = 0; i < OutputKnobs.Count; i++)
        {
            var angleKnob = AngleKnobs[i];
            var widthKnob = WidthKnobs[i];
            var outputKnob = OutputKnobs[i];

            var angle = Angles[i];
            var width = Widths[i];
            
            KnobValueField(angleKnob, ref angle, "Angle " + (i + 1));
            outputKnob.SetPosition();
            KnobValueField(widthKnob, ref width, "Width " + (i + 1));

            Angles[i] = angle;
            Widths[i] = width;
        }

        GUILayout.EndVertical();

        if (GUI.changed)
            canvas.OnNodeChange(this);
    }
    
    public override void FillNodeActionsMenu(NodeEditorInputInfo inputInfo, GenericMenu menu)
    {
        base.FillNodeActionsMenu(inputInfo, menu);
        menu.AddSeparator("");

        if (OutputKnobs.Count < 10)
        {
            menu.AddItem(new GUIContent("Add branch"), false, AddBranch);
            canvas.OnNodeChange(this);
        }

        if (OutputKnobs.Count > 2)
        {
            menu.AddItem(new GUIContent("Remove branch"), false, RemoveBranch);
        }
    }
    
    private void AddBranch()
    {
        var idx = OutputKnobs.Count;
        
        CreateValueConnectionKnob(new("Angle " + idx, Direction.In, ValueFunctionConnection.Id));
        CreateValueConnectionKnob(new("Width " + idx, Direction.In, ValueFunctionConnection.Id));
        CreateValueConnectionKnob(new("Output " + idx, Direction.Out, PathFunctionConnection.Id));
        
        Angles.Add(0);
        Widths.Add(1);
        
        RefreshDynamicKnobs();
        canvas.OnNodeChange(this);
    }

    private void RemoveBranch()
    {
        if (OutputKnobs.Count <= 2) return;
        
        var idx = OutputKnobs.Count - 1;
        
        DeleteConnectionPort(AngleKnobs[idx]);
        DeleteConnectionPort(WidthKnobs[idx]);
        DeleteConnectionPort(OutputKnobs[idx]);
        
        Angles.RemoveAt(idx);
        Widths.RemoveAt(idx);
        
        RefreshDynamicKnobs();
        canvas.OnNodeChange(this);
    }

    public override void RefreshPreview()
    {
        for (int i = 0; i < OutputKnobs.Count; i++)
        {
            GetIfConnected<double>(AngleKnobs[i])?.ResetState();
            GetIfConnected<double>(WidthKnobs[i])?.ResetState();
        }
        
        for (int i = 0; i < OutputKnobs.Count; i++)
        {
            var angle = GetIfConnected<double>(AngleKnobs[i]);
            var width = GetIfConnected<double>(WidthKnobs[i]);
            
            if (angle != null) Angles[i] = angle.Get();
            if (width != null) Widths[i] = width.Get();
        }
    }

    public override bool Calculate()
    {
        for (int i = 0; i < OutputKnobs.Count; i++)
        {
            OutputKnobs[i].SetValue<ISupplier<Path>>(new Output(
                SupplierOrFixed(InputKnob, Path.Empty),
                SupplierOrValueFixed(AngleKnobs[i], Angles[i]),
                SupplierOrValueFixed(WidthKnobs[i], Widths[i])
            ));
        }
        
        return true;
    }

    private class Output : ISupplier<Path>
    {
        private readonly ISupplier<Path> _input;
        private readonly ISupplier<double> _angle;
        private readonly ISupplier<double> _width;

        public Output(ISupplier<Path> input, ISupplier<double> angle, ISupplier<double> width)
        {
            _input = input;
            _angle = angle;
            _width = width;
        }

        public Path Get()
        {
            var path = new Path(_input.Get());
            
            foreach (var segment in path.Leaves())
            {
                segment.AttachNewBranch(_angle.Get(), _width.Get());
            }
            
            return path;
        }

        public void ResetState()
        {
            _input.ResetState();
            _angle.ResetState();
            _width.ResetState();
        }
    }
}
