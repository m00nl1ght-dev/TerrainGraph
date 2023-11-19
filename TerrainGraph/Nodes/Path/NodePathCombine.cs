using System;
using System.Collections.Generic;
using System.Linq;
using NodeEditorFramework;
using NodeEditorFramework.Utilities;
using UnityEngine;

namespace TerrainGraph;

[Serializable]
[Node(false, "Path/Combine", 608)]
public class NodePathCombine : NodeBase
{
    public const string ID = "pathCombine";
    public override string GetID => ID;

    public override string Title => "Path: Combine";

    [ValueConnectionKnob("Output", Direction.Out, PathFunctionConnection.Id)]
    public ValueConnectionKnob OutputKnob;

    [NonSerialized]
    public List<ValueConnectionKnob> InputKnobs = new();

    public override void RefreshDynamicKnobs()
    {
        InputKnobs = dynamicConnectionPorts.Where(k => k.name.StartsWith("Input")).Cast<ValueConnectionKnob>().ToList();
    }

    public override void NodeGUI()
    {
        OutputKnob.SetPosition(FirstKnobPosition);

        while (InputKnobs.Count < 2) AddInput();

        GUILayout.BeginVertical(BoxStyle);

        for (int i = 0; i < InputKnobs.Count; i++)
        {
            GUILayout.BeginHorizontal(BoxStyle);
            GUILayout.Label("Input " + (i + 1), BoxLayout);
            InputKnobs[i].SetPosition();
            GUILayout.EndHorizontal();
        }

        GUILayout.EndVertical();

        if (GUI.changed)
            canvas.OnNodeChange(this);
    }

    public override void FillNodeActionsMenu(NodeEditorInputInfo inputInfo, GenericMenu menu)
    {
        base.FillNodeActionsMenu(inputInfo, menu);
        menu.AddSeparator("");

        if (InputKnobs.Count < 20)
        {
            menu.AddItem(new GUIContent("Add input"), false, AddInput);
            canvas.OnNodeChange(this);
        }

        if (InputKnobs.Count > 2)
        {
            menu.AddItem(new GUIContent("Remove input"), false, RemoveInput);
        }
    }

    private void AddInput()
    {
        CreateValueConnectionKnob(new("Input " + InputKnobs.Count, Direction.In, PathFunctionConnection.Id));

        RefreshDynamicKnobs();
        canvas.OnNodeChange(this);
    }

    private void RemoveInput()
    {
        if (InputKnobs.Count <= 2) return;

        DeleteConnectionPort(InputKnobs[InputKnobs.Count - 1]);

        RefreshDynamicKnobs();
        canvas.OnNodeChange(this);
    }

    public override bool Calculate()
    {
        var inputs = InputKnobs.Select(input => SupplierOrFixed(input, Path.Empty)).ToList();

        OutputKnob.SetValue<ISupplier<Path>>(new Output(
            inputs
        ));

        return true;
    }

    private class Output : ISupplier<Path>
    {
        private readonly List<ISupplier<Path>> _inputs;

        public Output(List<ISupplier<Path>> inputs)
        {
            _inputs = inputs;
        }

        public Path Get()
        {
            if (_inputs.Count == 0) return Path.Empty;

            var paths = _inputs.Select(s => s.Get()).ToList();
            var output = new Path(paths[0]);

            for (var i = 1; i < paths.Count; i++)
            {
                output.Combine(paths[i]);
            }

            return output;
        }

        public void ResetState()
        {
            foreach (var input in _inputs) input.ResetState();
        }
    }
}
