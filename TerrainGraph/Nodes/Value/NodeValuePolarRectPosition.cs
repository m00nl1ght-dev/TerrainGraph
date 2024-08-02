using System;
using System.Collections.Generic;
using NodeEditorFramework;
using TerrainGraph.Util;
using UnityEngine;

namespace TerrainGraph;

[Serializable]
[Node(false, "Value/Polar Rect", 115)]
public class NodeValuePolarRectPosition : NodeBase
{
    public const string ID = "valuePolarRectPosition";
    public override string GetID => ID;

    public override string Title => "Polar Rect Position";

    [ValueConnectionKnob("Angle", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob AngleKnob;

    [ValueConnectionKnob("Offset", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob OffsetKnob;

    [ValueConnectionKnob("Margin", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob MarginKnob;

    [ValueConnectionKnob("OutputX", Direction.Out, ValueFunctionConnection.Id)]
    public ValueConnectionKnob OutputXKnob;

    [ValueConnectionKnob("OutputZ", Direction.Out, ValueFunctionConnection.Id)]
    public ValueConnectionKnob OutputZKnob;

    public double Angle;
    public double Offset;
    public double Margin;

    public override void NodeGUI()
    {
        GUILayout.BeginVertical(BoxStyle);

        KnobValueField(AngleKnob, ref Angle);

        OutputXKnob.SetPosition();

        KnobValueField(OffsetKnob, ref Offset);

        OutputZKnob.SetPosition();

        KnobValueField(MarginKnob, ref Margin);

        GUILayout.EndVertical();

        if (GUI.changed)
            canvas.OnNodeChange(this);
    }

    public override void RefreshPreview()
    {
        var angle = GetIfConnected<double>(AngleKnob);
        var offset = GetIfConnected<double>(OffsetKnob);
        var margin = GetIfConnected<double>(MarginKnob);

        angle?.ResetState();
        offset?.ResetState();
        margin?.ResetState();

        if (angle != null) Angle = angle.Get();
        if (offset != null) Offset = offset.Get();
        if (margin != null) Margin = margin.Get();
    }

    public override void CleanUpGUI()
    {
        if (AngleKnob.connected()) Angle = 0;
        if (OffsetKnob.connected()) Offset = 0;
        if (MarginKnob.connected()) Margin = 0;
    }

    public override bool Calculate()
    {
        var cache = new List<Vector2d>(5);

        var output = new Output(
            SupplierOrFallback(AngleKnob, Angle),
            SupplierOrFallback(OffsetKnob, Offset),
            SupplierOrFallback(MarginKnob, Margin)
        );

        OutputXKnob.SetValue<ISupplier<double>>(
            new Supplier.CompoundCached<Vector2d,double>(output, t => t.x, cache)
        );

        OutputZKnob.SetValue<ISupplier<double>>(
            new Supplier.CompoundCached<Vector2d,double>(output, t => t.z, cache)
        );

        return true;
    }

    private class Output : ISupplier<Vector2d>
    {
        protected readonly ISupplier<double> Angle;
        protected readonly ISupplier<double> Offset;
        protected readonly ISupplier<double> Margin;

        public Output(ISupplier<double> angle, ISupplier<double> offset, ISupplier<double> margin)
        {
            Angle = angle;
            Offset = offset;
            Margin = margin;
        }

        public Vector2d Get()
        {
            var angle = Angle.Get() - 90d;
            var offset = Offset.Get();
            var margin = Margin.Get();

            var vec = Vector2d.Direction(-angle);
            var pivot = new Vector2d(0.5, 0.5) + offset * vec.PerpCW;

            var divX = vec.x > 0 ? (1 - pivot.x) / vec.x : vec.x < 0 ? pivot.x / -vec.x : double.MaxValue;
            var divZ = vec.z > 0 ? (1 - pivot.z) / vec.z : vec.z < 0 ? pivot.z / -vec.z : double.MaxValue;

            return pivot + vec * (Math.Min(divX, divZ) + margin);
        }

        public void ResetState()
        {
            Angle.ResetState();
            Offset.ResetState();
            Margin.ResetState();
        }
    }
}
