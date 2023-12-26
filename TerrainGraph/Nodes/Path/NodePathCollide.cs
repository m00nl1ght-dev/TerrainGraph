using System;
using System.Linq;
using NodeEditorFramework;
using UnityEngine;

namespace TerrainGraph;

[Serializable]
[Node(false, "Path/Collide", 609)]
public class NodePathCollide : NodeBase
{
    public const string ID = "pathCollide";
    public override string GetID => ID;

    public override string Title => "Path: Collide";

    [ValueConnectionKnob("Input", Direction.In, PathFunctionConnection.Id)]
    public ValueConnectionKnob InputKnob;

    [ValueConnectionKnob("Arc Range", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob ArcRangeKnob;

    [ValueConnectionKnob("Stable Range", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob StableRangeKnob;

    [ValueConnectionKnob("Output", Direction.Out, PathFunctionConnection.Id)]
    public ValueConnectionKnob OutputKnob;

    public double ArcRange;
    public double StableRange;

    public override void NodeGUI()
    {
        InputKnob.SetPosition(FirstKnobPosition);
        OutputKnob.SetPosition(FirstKnobPosition);

        GUILayout.BeginVertical(BoxStyle);

        GUILayout.BeginHorizontal(BoxStyle);
        GUILayout.Label("Input", BoxLayout);
        GUILayout.EndHorizontal();

        KnobValueField(ArcRangeKnob, ref ArcRange);
        KnobValueField(StableRangeKnob, ref StableRange);

        GUILayout.EndVertical();

        if (GUI.changed)
            canvas.OnNodeChange(this);
    }

    public override void RefreshPreview()
    {
        var arcRange = GetIfConnected<double>(ArcRangeKnob);
        var stableRange = GetIfConnected<double>(StableRangeKnob);

        arcRange?.ResetState();
        stableRange?.ResetState();

        if (arcRange != null) ArcRange = arcRange.Get();
        if (stableRange != null) StableRange = stableRange.Get();
    }

    public override bool Calculate()
    {
        OutputKnob.SetValue<ISupplier<Path>>(new Output(
            SupplierOrFallback(InputKnob, Path.Empty),
            SupplierOrFallback(ArcRangeKnob, ArcRange),
            SupplierOrFallback(StableRangeKnob, StableRange)
        ));
        return true;
    }

    private class Output : ISupplier<Path>
    {
        private readonly ISupplier<Path> _input;
        private readonly ISupplier<double> _arcRange;
        private readonly ISupplier<double> _stableRange;

        public Output(ISupplier<Path> input, ISupplier<double> arcRange, ISupplier<double> stableRange)
        {
            _input = input;
            _arcRange = arcRange;
            _stableRange = stableRange;
        }

        public Path Get()
        {
            var path = new Path(_input.Get());

            foreach (var segment in path.Leaves.ToList())
            {
                var extParams = segment.TraceParams;

                extParams.ArcRetraceRange = _arcRange.Get();
                extParams.ArcStableRange = _stableRange.Get();

                segment.ExtendWithParams(extParams);
            }

            return path;
        }

        public void ResetState()
        {
            _input.ResetState();
            _arcRange.ResetState();
            _stableRange.ResetState();
        }
    }
}
