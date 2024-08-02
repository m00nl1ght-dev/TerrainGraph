using System;
using System.Linq;
using NodeEditorFramework;
using TerrainGraph.Flow;
using TerrainGraph.Util;
using UnityEngine;

namespace TerrainGraph;

[Serializable]
[Node(false, "Path/Tenacity", 602)]
public class NodePathTenacity : NodeBase
{
    public const string ID = "pathTenacity";
    public override string GetID => ID;

    public override string Title => "Path: Tenacity";

    [ValueConnectionKnob("Input", Direction.In, PathFunctionConnection.Id)]
    public ValueConnectionKnob InputKnob;

    [ValueConnectionKnob("Angle tenacity", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob AngleTenacityKnob;

    [ValueConnectionKnob("Split tenacity", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob SplitTenacityKnob;

    [ValueConnectionKnob("Angle limit abs", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob AngleLimitAbsKnob;

    [ValueConnectionKnob("Output", Direction.Out, PathFunctionConnection.Id)]
    public ValueConnectionKnob OutputKnob;

    public double AngleTenacity;
    public double SplitTenacity;
    public double AngleLimitAbs;

    public override void NodeGUI()
    {
        InputKnob.SetPosition(FirstKnobPosition);
        OutputKnob.SetPosition(FirstKnobPosition);

        GUILayout.BeginVertical(BoxStyle);

        GUILayout.BeginHorizontal(BoxStyle);
        GUILayout.Label("Input", BoxLayout);
        GUILayout.EndHorizontal();

        KnobValueField(AngleTenacityKnob, ref AngleTenacity);
        KnobValueField(SplitTenacityKnob, ref SplitTenacity);
        KnobValueField(AngleLimitAbsKnob, ref AngleLimitAbs);

        GUILayout.EndVertical();

        if (GUI.changed)
            canvas.OnNodeChange(this);
    }

    public override void RefreshPreview()
    {
        var angleTenacity = GetIfConnected<double>(AngleTenacityKnob);
        var splitTenacity = GetIfConnected<double>(SplitTenacityKnob);
        var angleLimitAbs = GetIfConnected<double>(AngleLimitAbsKnob);

        angleTenacity?.ResetState();
        splitTenacity?.ResetState();
        angleLimitAbs?.ResetState();

        if (angleTenacity != null) AngleTenacity = angleTenacity.Get();
        if (splitTenacity != null) SplitTenacity = splitTenacity.Get();
        if (angleLimitAbs != null) AngleLimitAbs = angleLimitAbs.Get();
    }

    public override void CleanUpGUI()
    {
        if (AngleTenacityKnob.connected()) AngleTenacity = 0;
        if (SplitTenacityKnob.connected()) SplitTenacity = 0;
        if (AngleLimitAbsKnob.connected()) AngleLimitAbs = 0;
    }

    public override bool Calculate()
    {
        OutputKnob.SetValue<ISupplier<Path>>(new Output(
            SupplierOrFallback(InputKnob, Path.Empty),
            SupplierOrFallback(AngleTenacityKnob, AngleTenacity),
            SupplierOrFallback(SplitTenacityKnob, SplitTenacity),
            SupplierOrFallback(AngleLimitAbsKnob, AngleLimitAbs)
        ));
        return true;
    }

    private class Output : ISupplier<Path>
    {
        private readonly ISupplier<Path> _input;
        private readonly ISupplier<double> _angleTenacity;
        private readonly ISupplier<double> _splitTenacity;
        private readonly ISupplier<double> _angleLimitAbs;

        public Output(
            ISupplier<Path> input,
            ISupplier<double> angleTenacity,
            ISupplier<double> splitTenacity,
            ISupplier<double> angleLimitAbs)
        {
            _input = input;
            _angleTenacity = angleTenacity;
            _splitTenacity = splitTenacity;
            _angleLimitAbs = angleLimitAbs;
        }

        public Path Get()
        {
            var path = new Path(_input.Get());

            foreach (var segment in path.Leaves.ToList())
            {
                var tenacity = _angleTenacity.Get().InRange01();
                var splitTenacity = _splitTenacity.Get().InRange01();
                var angleLimitAbs = _angleLimitAbs.Get().WithMin(0);

                var extParams = segment.TraceParams;

                extParams.AngleTenacity = tenacity;
                extParams.SplitTenacity = splitTenacity;
                extParams.AngleLimitAbs = angleLimitAbs;

                segment.ExtendWithParams(extParams);
            }

            return path;
        }

        public void ResetState()
        {
            _input.ResetState();
            _angleTenacity.ResetState();
            _splitTenacity.ResetState();
            _angleLimitAbs.ResetState();
        }
    }
}
