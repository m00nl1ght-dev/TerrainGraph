using System;
using System.Collections.Generic;
using System.Linq;
using NodeEditorFramework;
using NodeEditorFramework.Utilities;
using TerrainGraph.Flow;
using TerrainGraph.Util;
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
    public List<ValueConnectionKnob> WidthKnobs = [];

    [NonSerialized]
    public List<ValueConnectionKnob> AngleKnobs = [];

    [NonSerialized]
    public List<ValueConnectionKnob> SpeedKnobs = [];

    [NonSerialized]
    public List<ValueConnectionKnob> OutputKnobs = [];

    public List<double> Widths = [];
    public List<double> Angles = [];
    public List<double> Speeds = [];

    public override void RefreshDynamicKnobs()
    {
        WidthKnobs = dynamicConnectionPorts.Where(k => k.name.StartsWith("Width")).Cast<ValueConnectionKnob>().ToList();
        AngleKnobs = dynamicConnectionPorts.Where(k => k.name.StartsWith("Angle")).Cast<ValueConnectionKnob>().ToList();
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
            var angleKnob = AngleKnobs[i];
            var speedKnob = SpeedKnobs[i];
            var outputKnob = OutputKnobs[i];

            var width = Widths[i];
            var angle = Angles[i];
            var speed = Speeds[i];

            KnobValueField(widthKnob, ref width, "Width " + (i + 1));
            outputKnob.SetPosition();
            KnobValueField(angleKnob, ref angle, "Angle " + (i + 1));
            KnobValueField(speedKnob, ref speed, "Speed " + (i + 1));

            Widths[i] = width;
            Angles[i] = angle;
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
        CreateValueConnectionKnob(new("Angle " + idx, Direction.In, ValueFunctionConnection.Id));
        CreateValueConnectionKnob(new("Speed " + idx, Direction.In, ValueFunctionConnection.Id));
        CreateValueConnectionKnob(new("Output " + idx, Direction.Out, PathFunctionConnection.Id));

        Widths.Add(1);
        Angles.Add(0);
        Speeds.Add(1);

        RefreshDynamicKnobs();
        canvas.OnNodeChange(this);
    }

    private void RemoveBranch()
    {
        if (OutputKnobs.Count <= 2) return;

        var idx = OutputKnobs.Count - 1;

        DeleteConnectionPort(WidthKnobs[idx]);
        DeleteConnectionPort(AngleKnobs[idx]);
        DeleteConnectionPort(SpeedKnobs[idx]);
        DeleteConnectionPort(OutputKnobs[idx]);

        Widths.RemoveAt(idx);
        Angles.RemoveAt(idx);
        Speeds.RemoveAt(idx);

        RefreshDynamicKnobs();
        canvas.OnNodeChange(this);
    }

    public override void RefreshPreview()
    {
        for (int i = 0; i < OutputKnobs.Count; i++)
        {
            GetIfConnected<double>(WidthKnobs[i])?.ResetState();
            GetIfConnected<double>(AngleKnobs[i])?.ResetState();
            GetIfConnected<double>(SpeedKnobs[i])?.ResetState();
        }

        for (int i = 0; i < OutputKnobs.Count; i++)
        {
            var width = GetIfConnected<double>(WidthKnobs[i]);
            var angle = GetIfConnected<double>(AngleKnobs[i]);
            var speed = GetIfConnected<double>(SpeedKnobs[i]);

            if (width != null) Widths[i] = width.Get();
            if (angle != null) Angles[i] = angle.Get();
            if (speed != null) Speeds[i] = speed.Get();
        }
    }

    public override void CleanUpGUI()
    {
        for (int i = 0; i < WidthKnobs.Count; i++)
        {
            if (WidthKnobs[i].connected()) Widths[i] = 0;
        }

        for (int i = 0; i < AngleKnobs.Count; i++)
        {
            if (AngleKnobs[i].connected()) Angles[i] = 0;
        }

        for (int i = 0; i < SpeedKnobs.Count; i++)
        {
            if (SpeedKnobs[i].connected()) Speeds[i] = 0;
        }
    }

    public override void OnCreate(bool fromGUI)
    {
        base.OnCreate(fromGUI);

        while (WidthKnobs.Count < OutputKnobs.Count)
            WidthKnobs.Add(CreateValueConnectionKnob(new("Width " + WidthKnobs.Count, Direction.In, ValueFunctionConnection.Id)));
        while (AngleKnobs.Count < OutputKnobs.Count)
            AngleKnobs.Add(CreateValueConnectionKnob(new("Angle " + AngleKnobs.Count, Direction.In, ValueFunctionConnection.Id)));
        while (SpeedKnobs.Count < OutputKnobs.Count)
            SpeedKnobs.Add(CreateValueConnectionKnob(new("Speed " + SpeedKnobs.Count, Direction.In, ValueFunctionConnection.Id)));

        while (Widths.Count < OutputKnobs.Count) Widths.Add(1);
        while (Angles.Count < OutputKnobs.Count) Angles.Add(0);
        while (Speeds.Count < OutputKnobs.Count) Speeds.Add(1);
    }

    public override bool Calculate()
    {
        var input = SupplierOrFallback(InputKnob, Path.Empty);
        var widths = new ISupplier<double>[OutputKnobs.Count];
        var angles = new ISupplier<double>[OutputKnobs.Count];
        var speeds = new ISupplier<double>[OutputKnobs.Count];

        for (int i = 0; i < OutputKnobs.Count; i++)
        {
            widths[i] = SupplierOrFallback(WidthKnobs[i], Widths[i]);
            angles[i] = SupplierOrFallback(AngleKnobs[i], Angles[i]);
            speeds[i] = SupplierOrFallback(SpeedKnobs[i], Speeds[i]);
        }

        var cache = new List<Path[]>(5);

        var output = new Output(input, widths, angles, speeds);

        for (int i = 0; i < OutputKnobs.Count; i++)
        {
            var index = i;
            OutputKnobs[i].SetValue<ISupplier<Path>>(new Supplier.CompoundCached<Path[],Path>(output, t => t[index], cache));
        }

        return true;
    }

    private class Output : ISupplier<Path[]>
    {
        private readonly ISupplier<Path> _input;
        private readonly ISupplier<double>[] _widths;
        private readonly ISupplier<double>[] _angles;
        private readonly ISupplier<double>[] _speeds;

        public Output(
            ISupplier<Path> input,
            ISupplier<double>[] widths,
            ISupplier<double>[] angles,
            ISupplier<double>[] speeds)
        {
            _input = input;
            _widths = widths;
            _angles = angles;
            _speeds = speeds;
        }

        public Path[] Get()
        {
            var branchCount = _widths.Length;

            var input = _input.Get();
            var paths = new Path[branchCount];

            for (int i = 0; i < branchCount; i++) paths[i] = new Path(input);

            var widths = new double[branchCount];
            var angles = new double[branchCount];
            var speeds = new double[branchCount];

            var leafIds = input.Leaves.Select(s => s.Id);

            foreach (var leafId in leafIds)
            {
                for (int i = 0; i < branchCount; i++)
                {
                    widths[i] = _widths[i].Get();
                    angles[i] = _angles[i].Get();
                    speeds[i] = _speeds[i].Get();
                }

                for (int i = 0; i < branchCount; i++)
                {
                    if (widths[i] < 0)
                    {
                        widths[i] = 1 - widths.Where(w => w > 0).Sum().WithMax(1);
                    }
                }

                var rwEnds = widths[0] + widths[branchCount - 1];
                var rwMid = widths.Sum() - rwEnds;

                var rwPos = 0d;

                for (var i = 0; i < branchCount; i++)
                {
                    var width = widths[i];

                    double mdPos;

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

                    var branch = paths[i].Segments[leafId].AttachNew();

                    branch.RelWidth = widths[i];
                    branch.RelSpeed = speeds[i];
                    branch.RelShift = mdPos - 0.5;
                    branch.InitialAngleDeltaMin = angles[i];
                }
            }

            return paths;
        }

        public void ResetState()
        {
            _input.ResetState();
            foreach (var supplier in _widths) supplier.ResetState();
            foreach (var supplier in _angles) supplier.ResetState();
            foreach (var supplier in _speeds) supplier.ResetState();
        }
    }
}
