using System;
using System.Collections.Generic;
using System.Linq;
using NodeEditorFramework;
using NodeEditorFramework.Utilities;
using UnityEngine;

namespace TerrainGraph;

[Serializable]
public abstract class NodeOperatorBase : NodeBase
{
    public override string Title => OperationType.ToString().Replace('_', ' ');

    private static readonly ValueConnectionKnobAttribute ApplyChanceAttribute = new("Apply chance", Direction.In, ValueFunctionConnection.Id);
    private static readonly ValueConnectionKnobAttribute StackCountAttribute = new("Stack count", Direction.In, ValueFunctionConnection.Id);
    private static readonly ValueConnectionKnobAttribute SmoothnessAttribute = new("Smoothness", Direction.In, ValueFunctionConnection.Id);

    [NonSerialized]
    public ValueConnectionKnob ApplyChanceKnob;

    [NonSerialized]
    public ValueConnectionKnob StackCountKnob;

    [NonSerialized]
    public ValueConnectionKnob SmoothnessKnob;

    public abstract ValueConnectionKnob OutputKnobRef { get; }

    public List<double> Values = [];

    public Operation OperationType = Operation.Add;
    public double ApplyChance = 1f;
    public double StackCount = 1;
    public double Smoothness;

    [NonSerialized]
    public List<ValueConnectionKnob> InputKnobs = [];

    public override void RefreshDynamicKnobs()
    {
        InputKnobs = dynamicConnectionPorts.Where(k => k.name.StartsWith("Input")).Cast<ValueConnectionKnob>().ToList();
        ApplyChanceKnob = FindDynamicKnob(ApplyChanceAttribute);
        StackCountKnob = FindDynamicKnob(StackCountAttribute);
        SmoothnessKnob = FindDynamicKnob(SmoothnessAttribute);
    }

    public override void NodeGUI()
    {
        OutputKnobRef.SetPosition(FirstKnobPosition);
        while (InputKnobs.Count < 2) CreateNewInputKnob();

        GUILayout.BeginVertical(BoxStyle);

        if (SmoothnessKnob != null) KnobValueField(SmoothnessKnob, ref Smoothness);
        if (ApplyChanceKnob != null) KnobValueField(ApplyChanceKnob, ref ApplyChance);
        if (StackCountKnob != null) KnobValueField(StackCountKnob, ref StackCount);

        while (Values.Count < InputKnobs.Count) Values.Add(0f);
        while (Values.Count > InputKnobs.Count) Values.RemoveAt(Values.Count - 1);

        for (int i = 0; i < InputKnobs.Count; i++) InputGUI(i);

        GUILayout.EndVertical();

        if (GUI.changed)
            canvas.OnNodeChange(this);
    }

    protected virtual void InputGUI(int i)
    {
        GUILayout.BeginHorizontal(BoxStyle);
        GUILayout.Label(i == 0 ? "Base" : ("Input " + i), BoxLayout);

        if (!InputKnobs[i].connected())
        {
            GUILayout.FlexibleSpace();
            Values[i] = RTEditorGUI.FloatField(GUIContent.none, (float) Values[i], BoxLayout);
        }

        GUILayout.EndHorizontal();
        InputKnobs[i].SetPosition();
    }

    protected abstract void CreateNewInputKnob();

    public override void FillNodeActionsMenu(NodeEditorInputInfo inputInfo, GenericMenu menu)
    {
        base.FillNodeActionsMenu(inputInfo, menu);
        menu.AddSeparator("");

        SelectionMenu<Operation>(menu, SetOperation, "Change operation/");

        if (ApplyChanceKnob != null)
        {
            menu.AddItem(new GUIContent("Remove apply chance"), false, () =>
            {
                DeleteConnectionPort(ApplyChanceKnob);
                RefreshDynamicKnobs();
                ApplyChance = 1f;
                canvas.OnNodeChange(this);
            });
        }
        else
        {
            menu.AddItem(new GUIContent("Add apply chance"), false, () =>
            {
                ApplyChanceKnob ??= (ValueConnectionKnob) CreateConnectionKnob(ApplyChanceAttribute);
                canvas.OnNodeChange(this);
            });
        }

        if (StackCountKnob != null)
        {
            menu.AddItem(new GUIContent("Remove stacking"), false, () =>
            {
                DeleteConnectionPort(StackCountKnob);
                RefreshDynamicKnobs();
                StackCount = 0;
                canvas.OnNodeChange(this);
            });
        }
        else
        {
            menu.AddItem(new GUIContent("Add stacking"), false, () =>
            {
                StackCountKnob ??= (ValueConnectionKnob) CreateConnectionKnob(StackCountAttribute);
                canvas.OnNodeChange(this);
            });
        }

        menu.AddSeparator("");

        if (InputKnobs.Count < 20)
        {
            menu.AddItem(new GUIContent("Add input"), false, CreateNewInputKnob);
            canvas.OnNodeChange(this);
        }

        if (InputKnobs.Count > 2)
        {
            menu.AddItem(new GUIContent("Remove input"), false, () =>
            {
                DeleteConnectionPort(InputKnobs[InputKnobs.Count - 1]);
                RefreshDynamicKnobs();
                canvas.OnNodeChange(this);
            });
        }
    }

    protected void SetOperation(Operation operation)
    {
        OperationType = operation;

        if (operation is Operation.Smooth_Min or Operation.Smooth_Max)
        {
            SmoothnessKnob ??= (ValueConnectionKnob) CreateConnectionKnob(SmoothnessAttribute);
        }
        else if (SmoothnessKnob != null)
        {
            DeleteConnectionPort(SmoothnessKnob);
            RefreshDynamicKnobs();
            Smoothness = 0;
        }

        canvas.OnNodeChange(this);
    }

    public override void RefreshPreview()
    {
        var chance = GetIfConnected<double>(ApplyChanceKnob);
        var stack = GetIfConnected<double>(StackCountKnob);
        var smooth = GetIfConnected<double>(SmoothnessKnob);

        chance?.ResetState();
        stack?.ResetState();
        smooth?.ResetState();

        if (chance != null) ApplyChance = chance.Get();
        if (stack != null) StackCount = stack.Get();
        if (smooth != null) Smoothness = smooth.Get();
    }

    public enum Operation
    {
        Add,
        Subtract,
        Multiply,
        Divide,
        Min,
        Max,
        Smooth_Min,
        Smooth_Max,
        Invert,
        Invert_Below,
        Invert_Above,
        Scale_Around_1
    }
}
