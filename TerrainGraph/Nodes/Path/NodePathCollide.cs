using System;
using System.Linq;
using NodeEditorFramework;
using TerrainGraph.Flow;
using UnityEngine;

namespace TerrainGraph;

[Serializable]
[Node(false, "Path/Collision", 609)]
public class NodePathCollide : NodeBase
{
    public const string ID = "pathCollision";
    public override string GetID => ID;

    public override string Title => "Path: Collision";

    [ValueConnectionKnob("Input", Direction.In, PathFunctionConnection.Id)]
    public ValueConnectionKnob InputKnob;

    [ValueConnectionKnob("Arc range", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob ArcRangeKnob;

    [ValueConnectionKnob("Arc intensity", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob ArcIntensityKnob;

    [ValueConnectionKnob("Stable range", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob StableRangeKnob;

    [ValueConnectionKnob("Trim merged", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob MergeResultTrimKnob;

    [ValueConnectionKnob("Split turn lock", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob SplitTurnLockKnob;

    [ValueConnectionKnob("Output", Direction.Out, PathFunctionConnection.Id)]
    public ValueConnectionKnob OutputKnob;

    public double ArcRange;
    public double ArcIntensity;
    public double StableRange;
    public double MergeResultTrim;
    public double SplitTurnLock;

    public override void NodeGUI()
    {
        InputKnob.SetPosition(FirstKnobPosition);
        OutputKnob.SetPosition(FirstKnobPosition);

        GUILayout.BeginVertical(BoxStyle);

        GUILayout.BeginHorizontal(BoxStyle);
        GUILayout.Label("Input", BoxLayout);
        GUILayout.EndHorizontal();

        KnobValueField(ArcRangeKnob, ref ArcRange);
        KnobValueField(ArcIntensityKnob, ref ArcIntensity);
        KnobValueField(StableRangeKnob, ref StableRange);
        KnobValueField(MergeResultTrimKnob, ref MergeResultTrim);
        KnobValueField(SplitTurnLockKnob, ref SplitTurnLock);

        GUILayout.EndVertical();

        if (GUI.changed)
            canvas.OnNodeChange(this);
    }

    public override void RefreshPreview()
    {
        var arcRange = GetIfConnected<double>(ArcRangeKnob);
        var arcIntensity = GetIfConnected<double>(ArcIntensityKnob);
        var stableRange = GetIfConnected<double>(StableRangeKnob);
        var mergeResultTrim = GetIfConnected<double>(MergeResultTrimKnob);
        var splitTurnLock = GetIfConnected<double>(SplitTurnLockKnob);

        arcRange?.ResetState();
        arcIntensity?.ResetState();
        stableRange?.ResetState();
        mergeResultTrim?.ResetState();
        splitTurnLock?.ResetState();

        if (arcRange != null) ArcRange = arcRange.Get();
        if (arcIntensity != null) ArcIntensity = arcIntensity.Get();
        if (stableRange != null) StableRange = stableRange.Get();
        if (mergeResultTrim != null) MergeResultTrim = mergeResultTrim.Get();
        if (splitTurnLock != null) SplitTurnLock = splitTurnLock.Get();
    }

    public override void CleanUpGUI()
    {
        if (ArcRangeKnob.connected()) ArcRange = 0;
        if (ArcIntensityKnob.connected()) ArcIntensity = 0;
        if (StableRangeKnob.connected()) StableRange = 0;
        if (MergeResultTrimKnob.connected()) MergeResultTrim = 0;
        if (SplitTurnLockKnob.connected()) SplitTurnLock = 0;
    }

    public override bool Calculate()
    {
        OutputKnob.SetValue<ISupplier<Path>>(new Output(
            SupplierOrFallback(InputKnob, Path.Empty),
            SupplierOrFallback(ArcRangeKnob, ArcRange),
            SupplierOrFallback(ArcIntensityKnob, ArcIntensity),
            SupplierOrFallback(StableRangeKnob, StableRange),
            SupplierOrFallback(MergeResultTrimKnob, MergeResultTrim),
            SupplierOrFallback(SplitTurnLockKnob, SplitTurnLock)
        ));
        return true;
    }

    private class Output : ISupplier<Path>
    {
        private readonly ISupplier<Path> _input;
        private readonly ISupplier<double> _arcRange;
        private readonly ISupplier<double> _arcIntensity;
        private readonly ISupplier<double> _stableRange;
        private readonly ISupplier<double> _mergeResultTrim;
        private readonly ISupplier<double> _splitTurnLock;

        public Output(
            ISupplier<Path> input,
            ISupplier<double> arcRange,
            ISupplier<double> arcIntensity,
            ISupplier<double> stableRange,
            ISupplier<double> mergeResultTrim,
            ISupplier<double> splitTurnLock)
        {
            _input = input;
            _arcRange = arcRange;
            _arcIntensity = arcIntensity;
            _stableRange = stableRange;
            _mergeResultTrim = mergeResultTrim;
            _splitTurnLock = splitTurnLock;
        }

        public Path Get()
        {
            var path = new Path(_input.Get());

            foreach (var segment in path.Leaves.ToList())
            {
                var extParams = segment.TraceParams;

                extParams.ArcRetraceRange = _arcRange.Get();
                extParams.ArcRetraceFactor = _arcIntensity.Get();
                extParams.ArcStableRange = _stableRange.Get();
                extParams.MergeResultTrim = _mergeResultTrim.Get();
                extParams.SplitTurnLock = _splitTurnLock.Get();

                segment.ExtendWithParams(extParams);
            }

            return path;
        }

        public void ResetState()
        {
            _input.ResetState();
            _arcRange.ResetState();
            _arcIntensity.ResetState();
            _stableRange.ResetState();
            _mergeResultTrim.ResetState();
            _splitTurnLock.ResetState();
        }
    }
}
