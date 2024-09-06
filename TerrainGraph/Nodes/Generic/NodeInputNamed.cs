using System;
using System.Linq;
using NodeEditorFramework;
using NodeEditorFramework.Utilities;
using UnityEngine;

namespace TerrainGraph;

[Serializable]
[Node(false, "Input/Named", 350)]
public class NodeInputNamed : NodeBase
{
    public const string ID = "inputNamed";
    public override string GetID => ID;

    public override string Title => "Named Input";

    [NonSerialized]
    public ValueConnectionKnob ValueKnob;

    public string TypeId = ValueFunctionConnection.Id;
    public string Name = "unnamed";

    [NonSerialized]
    internal object ValueSupplier;

    public override void RefreshDynamicKnobs()
    {
        ValueKnob = dynamicConnectionPorts.FirstOrDefault() as ValueConnectionKnob;
        ValueKnob ??= CreateValueConnectionKnob(new("Value", Direction.Out, TypeId));
    }

    public override void NodeGUI()
    {
        GUILayout.BeginVertical(BoxStyle);

        GUILayout.BeginHorizontal(BoxStyle);
        Name = RTEditorGUI.TextField(Name, FullBoxLayout);
        GUILayout.EndHorizontal();
        ValueKnob?.SetPosition();

        GUILayout.EndVertical();

        if (GUI.changed)
            canvas.OnNodeChange(this);
    }

    public override void FillNodeActionsMenu(NodeEditorInputInfo inputInfo, GenericMenu menu)
    {
        base.FillNodeActionsMenu(inputInfo, menu);
        menu.AddSeparator("");

        var types = ConnectionPortStyles.ValueConnectionTypes.ToList();

        SelectionMenu(menu, types, SetType, t => $"Change type/{t.Identifier}");
    }

    private void SetType(ValueConnectionType type)
    {
        TypeId = type.Identifier;
        if (ValueKnob != null) DeleteConnectionPort(ValueKnob);
        ValueKnob = CreateValueConnectionKnob(new("Value", Direction.Out, TypeId));
    }

    public override void OnCreate(bool fromGUI)
    {
        base.OnCreate(fromGUI);
        TerrainCanvas.NamedInputs.Add(this);
    }

    protected internal override void OnDelete()
    {
        base.OnDelete();
        TerrainCanvas.NamedInputs.Remove(this);
    }

    public override bool Calculate()
    {
        ValueKnob.SetValue(ValueSupplier);
        return true;
    }

    public void Set<T>(ISupplier<T> supplier)
    {
        ValueSupplier = supplier;
    }
}
