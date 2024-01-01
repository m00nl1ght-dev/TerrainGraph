using System;
using System.Collections.Generic;
using System.Linq;
using NodeEditorFramework;
using NodeEditorFramework.Utilities;
using UnityEngine;

namespace TerrainGraph;

[Serializable]
[Node(false, "Path/Split", 608)]
public class NodePathSplit : NodeBase
{
    public const string ID = "pathSplit";
    public override string GetID => ID;

    public override string Title => "Path: Split";

    [ValueConnectionKnob("Input", Direction.In, PathFunctionConnection.Id)]
    public ValueConnectionKnob InputKnob;

    [NonSerialized]
    public List<ValueConnectionKnob> WidthKnobs = new();

    [NonSerialized]
    public List<ValueConnectionKnob> SpeedKnobs = new();

    [NonSerialized]
    public List<ValueConnectionKnob> OutputKnobs = new();

    public List<double> Widths = new();
    public List<double> Speeds = new();

    public override void RefreshDynamicKnobs()
    {
        WidthKnobs = dynamicConnectionPorts.Where(k => k.name.StartsWith("Width")).Cast<ValueConnectionKnob>().ToList();
        SpeedKnobs = dynamicConnectionPorts.Where(k => k.name.StartsWith("Speed")).Cast<ValueConnectionKnob>().ToList();
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
            var widthKnob = WidthKnobs[i];
            var speedKnob = SpeedKnobs[i];
            var outputKnob = OutputKnobs[i];

            var width = Widths[i];
            var speed = Speeds[i];

            KnobValueField(widthKnob, ref width, "Width " + (i + 1));
            outputKnob.SetPosition();
            KnobValueField(speedKnob, ref speed, "Speed " + (i + 1));

            Widths[i] = width;
            Speeds[i] = speed;
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

        CreateValueConnectionKnob(new("Width " + idx, Direction.In, ValueFunctionConnection.Id));
        CreateValueConnectionKnob(new("Speed " + idx, Direction.In, ValueFunctionConnection.Id));
        CreateValueConnectionKnob(new("Output " + idx, Direction.Out, PathFunctionConnection.Id));

        Widths.Add(1);
        Speeds.Add(1);

        RefreshDynamicKnobs();
        canvas.OnNodeChange(this);
    }

    private void RemoveBranch()
    {
        if (OutputKnobs.Count <= 2) return;

        var idx = OutputKnobs.Count - 1;

        DeleteConnectionPort(WidthKnobs[idx]);
        DeleteConnectionPort(SpeedKnobs[idx]);
        DeleteConnectionPort(OutputKnobs[idx]);

        Widths.RemoveAt(idx);
        Speeds.RemoveAt(idx);

        RefreshDynamicKnobs();
        canvas.OnNodeChange(this);
    }

    public override void RefreshPreview()
    {
        for (int i = 0; i < OutputKnobs.Count; i++)
        {
            GetIfConnected<double>(WidthKnobs[i])?.ResetState();
            GetIfConnected<double>(SpeedKnobs[i])?.ResetState();
        }

        for (int i = 0; i < OutputKnobs.Count; i++)
        {
            var width = GetIfConnected<double>(WidthKnobs[i]);
            var speed = GetIfConnected<double>(SpeedKnobs[i]);

            if (width != null) Widths[i] = width.Get();
            if (speed != null) Speeds[i] = speed.Get();
        }
    }

    public override bool Calculate()
    {
        var input = SupplierOrFallback(InputKnob, Path.Empty);
        var widths = new ISupplier<double>[OutputKnobs.Count];
        var speeds = new ISupplier<double>[OutputKnobs.Count];

        for (int i = 0; i < OutputKnobs.Count; i++)
        {
            widths[i] = SupplierOrFallback(WidthKnobs[i], Widths[i]);
            speeds[i] = SupplierOrFallback(SpeedKnobs[i], Speeds[i]);
        }

        for (int i = 0; i < OutputKnobs.Count; i++)
        {
            OutputKnobs[i].SetValue<ISupplier<Path>>(new Output(
                input, widths, speeds, i
            ));
        }

        return true;
    }

    [HotSwappable]
    private class Output : ISupplier<Path>
    {
        private readonly ISupplier<Path> _input;
        private readonly ISupplier<double>[] _widths;
        private readonly ISupplier<double>[] _speeds;
        private readonly int _index;

        public Output(
            ISupplier<Path> input,
            ISupplier<double>[] widths,
            ISupplier<double>[] speeds,
            int index)
        {
            _input = input;
            _widths = widths;
            _speeds = speeds;
            _index = index;
        }

        public Path Get()
        {
            var branchCount = _widths.Length;

            var path = new Path(_input.ResetAndGet());

            var widths = new double[branchCount];
            var speeds = new double[branchCount];

            for (int i = 0; i < branchCount; i++)
            {
                widths[i] = _widths[i].Get();
                speeds[i] = _speeds[i].Get();
            }

            var rwEnds = widths[0] + widths[branchCount - 1];
            var rwMid = widths.Sum() - rwEnds;

            var rwPos = 0d;
            var mdPos = 0d;

            for (var i = 0; i <= _index; i++)
            {
                var width = widths[i];

                if (i == 0)
                {
                    mdPos = rwPos + width / 2;
                    rwPos += width;
                }
                else if (i == branchCount - 1)
                {
                    mdPos = 1 - width / 2;
                }
                else
                {
                    var rw = width * (1 - rwEnds) / rwMid;
                    mdPos = rwPos + rw / 2;
                    rwPos += rw;
                }
            }

            foreach (var segment in path.Leaves.ToList())
            {
                var branch = segment.AttachNew();

                branch.RelWidth = widths[_index];
                branch.RelSpeed = speeds[_index];
                branch.RelShift = mdPos - 0.5;

                var stableRange = segment.TraceParams.ArcStableRange;

                segment.ApplyLocalStabilityAtHead(0, stableRange / 2);
                branch.ApplyLocalStabilityAtTail(stableRange / 2, stableRange / 2);
            }

            return path;
        }

        public void ResetState()
        {
            foreach (var supplier in _widths) supplier.ResetState();
            foreach (var supplier in _speeds) supplier.ResetState();
        }
    }
}
