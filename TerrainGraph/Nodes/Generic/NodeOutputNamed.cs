using System;
using System.Linq;
using NodeEditorFramework;
using NodeEditorFramework.Utilities;
using UnityEngine;

namespace TerrainGraph;

[Serializable]
[Node(false, "Output/Named", 400)]
public class NodeOutputNamed : NodeBase
{
    public const string ID = "outputNamed";
    public override string GetID => ID;

    public override string Title => "Named Output";

    [NonSerialized]
    public ValueConnectionKnob ValueKnob;

    public string TypeId = ValueFunctionConnection.Id;
    public string Name = "unnamed";

    public override void RefreshDynamicKnobs()
    {
        ValueKnob = dynamicConnectionPorts.FirstOrDefault() as ValueConnectionKnob;
        ValueKnob ??= CreateValueConnectionKnob(new("Value", Direction.In, TypeId));
    }

    public override void NodeGUI()
    {
        GUILayout.BeginVertical(BoxStyle);

        GUILayout.BeginHorizontal(BoxStyle);
        Name = RTEditorGUI.TextField(Name, FullBoxLayout);
        GUILayout.EndHorizontal();
        ValueKnob?.SetPosition();

        GUILayout.EndVertical();
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
        ValueKnob = CreateValueConnectionKnob(new("Value", Direction.In, TypeId));
    }

    public override void OnCreate(bool fromGUI)
    {
        base.OnCreate(fromGUI);
        TerrainCanvas.NamedOutputs.Add(this);
    }

    protected internal override void OnDelete()
    {
        base.OnDelete();
        TerrainCanvas.NamedOutputs.Remove(this);
    }

    public ISupplier<T> Get<T>()
    {
        return ValueKnob.GetValue<ISupplier<T>>() ?? Supplier.Of<T>(default);
    }
}
