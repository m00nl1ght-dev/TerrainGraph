using System;
using NodeEditorFramework;
using NodeEditorFramework.Utilities;
using UnityEngine;

namespace TerrainGraph;

[Serializable]
[Node(false, "Value/Const", 105)]
public class NodeValueConst : NodeBase
{
    public const string ID = "valueConst";
    public override string GetID => ID;

    public override Vector2 DefaultSize => new(100, 55);
    public override bool AutoLayout => false;

    public override string Title => "Const";

    [ValueConnectionKnob("Output", Direction.Out, ValueFunctionConnection.Id)]
    public ValueConnectionKnob OutputKnob;

    public double Value;

    public override void NodeGUI()
    {
        OutputKnob.SetPosition(FirstKnobPosition);

        GUILayout.BeginVertical(BoxStyle);
        GUILayout.BeginHorizontal(BoxStyle);

        Value = RTEditorGUI.FloatField(GUIContent.none, (float) Value, FullBoxLayout);
        
        GUILayout.EndHorizontal();
        GUILayout.EndVertical();

        if (GUI.changed)
            canvas.OnNodeChange(this);
    }

    public override bool Calculate()
    {
        OutputKnob.SetValue<ISupplier<double>>(Supplier.Of(Value));
        return true;
    }
}
