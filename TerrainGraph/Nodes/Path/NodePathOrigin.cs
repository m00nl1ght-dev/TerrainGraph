using System;
using NodeEditorFramework;
using NodeEditorFramework.Utilities;
using TerrainGraph.Util;
using UnityEngine;

namespace TerrainGraph;

[HotSwappable]
[Serializable]
[Node(false, "Path/Origin", 600)]
public class NodePathOrigin : NodeBase
{
    public const string ID = "pathOrigin";
    public override string GetID => ID;

    public override string Title => "Path: Origin";

    private static readonly ValueConnectionKnobAttribute AngleAttribute = new("Angle", NodeEditorFramework.Direction.In, ValueFunctionConnection.Id);
    private static readonly ValueConnectionKnobAttribute OffsetAttribute = new("Offset", NodeEditorFramework.Direction.In, ValueFunctionConnection.Id);
    private static readonly ValueConnectionKnobAttribute MarginAttribute = new("Margin", NodeEditorFramework.Direction.In, ValueFunctionConnection.Id);
    private static readonly ValueConnectionKnobAttribute PosXAttribute = new("Position x", NodeEditorFramework.Direction.In, ValueFunctionConnection.Id);
    private static readonly ValueConnectionKnobAttribute PosZAttribute = new("Position z", NodeEditorFramework.Direction.In, ValueFunctionConnection.Id);

    [NonSerialized]
    public ValueConnectionKnob AngleKnob;

    [NonSerialized]
    public ValueConnectionKnob OffsetKnob;

    [NonSerialized]
    public ValueConnectionKnob MarginKnob;

    [NonSerialized]
    public ValueConnectionKnob PosXKnob;

    [NonSerialized]
    public ValueConnectionKnob PosZKnob;

    [ValueConnectionKnob("Direction", NodeEditorFramework.Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob DirectionKnob;

    [ValueConnectionKnob("Width", NodeEditorFramework.Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob WidthKnob;

    [ValueConnectionKnob("Value", NodeEditorFramework.Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob ValueKnob;

    [ValueConnectionKnob("Speed", NodeEditorFramework.Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob SpeedKnob;

    [ValueConnectionKnob("Density", NodeEditorFramework.Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob DensityKnob;

    [ValueConnectionKnob("Count", NodeEditorFramework.Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob CountKnob;

    [ValueConnectionKnob("Output", NodeEditorFramework.Direction.Out, PathFunctionConnection.Id)]
    public ValueConnectionKnob OutputKnob;

    public PlacementMode Mode = PlacementMode.Position_XZ;

    public double Angle;
    public double Offset;
    public double Margin;
    public double PosX = 0.5;
    public double PosZ = 0.5;
    public double Direction;
    public double Width = 10;
    public double Value;
    public double Speed = 1;
    public double Density = 1;
    public double Count = 1;

    public override void RefreshDynamicKnobs()
    {
        AngleKnob = FindDynamicKnob(AngleAttribute);
        OffsetKnob = FindDynamicKnob(OffsetAttribute);
        MarginKnob = FindDynamicKnob(MarginAttribute);
        PosXKnob = FindDynamicKnob(PosXAttribute);
        PosZKnob = FindDynamicKnob(PosZAttribute);
    }

    public override void NodeGUI()
    {
        OutputKnob.SetPosition(FirstKnobPosition);

        GUILayout.BeginVertical(BoxStyle);

        if (AngleKnob != null) KnobValueField(AngleKnob, ref Angle);
        if (OffsetKnob != null) KnobValueField(OffsetKnob, ref Offset);
        if (MarginKnob != null) KnobValueField(MarginKnob, ref Margin);
        if (PosXKnob != null) KnobValueField(PosXKnob, ref PosX);
        if (PosZKnob != null) KnobValueField(PosZKnob, ref PosZ);

        KnobValueField(DirectionKnob, ref Direction);
        KnobValueField(WidthKnob, ref Width);
        KnobValueField(ValueKnob, ref Value);
        KnobValueField(SpeedKnob, ref Speed);
        KnobValueField(DensityKnob, ref Density);
        KnobValueField(CountKnob, ref Count);

        GUILayout.EndVertical();

        if (GUI.changed)
            canvas.OnNodeChange(this);
    }

    public override void FillNodeActionsMenu(NodeEditorInputInfo inputInfo, GenericMenu menu)
    {
        base.FillNodeActionsMenu(inputInfo, menu);

        menu.AddSeparator("");

        SelectionMenu<PlacementMode>(menu, SetPlacementMode, "Change placement mode/");
    }

    public override void PrepareGUI()
    {
        UpdateDynamicKnobs();
    }

    protected void SetPlacementMode(PlacementMode mode)
    {
        Mode = mode;
        UpdateDynamicKnobs();
        canvas.OnNodeChange(this);
    }

    protected void UpdateDynamicKnobs()
    {
        if (Mode == PlacementMode.Grid_Edge)
        {
            AngleKnob ??= (ValueConnectionKnob) CreateConnectionKnob(AngleAttribute);
            OffsetKnob ??= (ValueConnectionKnob) CreateConnectionKnob(OffsetAttribute);
            MarginKnob ??= (ValueConnectionKnob) CreateConnectionKnob(MarginAttribute);

            if (PosXKnob != null) DeleteConnectionPort(PosXKnob);
            if (PosZKnob != null) DeleteConnectionPort(PosZKnob);
        }
        else
        {
            PosXKnob ??= (ValueConnectionKnob) CreateConnectionKnob(PosXAttribute);
            PosZKnob ??= (ValueConnectionKnob) CreateConnectionKnob(PosZAttribute);

            if (AngleKnob != null) DeleteConnectionPort(AngleKnob);
            if (OffsetKnob != null) DeleteConnectionPort(OffsetKnob);
            if (MarginKnob != null) DeleteConnectionPort(MarginKnob);
        }

        RefreshDynamicKnobs();
    }

    public override void RefreshPreview()
    {
        var angle = GetIfConnected<double>(AngleKnob);
        var offset = GetIfConnected<double>(OffsetKnob);
        var margin = GetIfConnected<double>(MarginKnob);
        var posX = GetIfConnected<double>(PosXKnob);
        var posZ = GetIfConnected<double>(PosZKnob);
        var direction = GetIfConnected<double>(DirectionKnob);
        var width = GetIfConnected<double>(WidthKnob);
        var value = GetIfConnected<double>(ValueKnob);
        var speed = GetIfConnected<double>(SpeedKnob);
        var density = GetIfConnected<double>(DensityKnob);
        var count = GetIfConnected<double>(CountKnob);

        angle?.ResetState();
        offset?.ResetState();
        margin?.ResetState();
        posX?.ResetState();
        posZ?.ResetState();
        direction?.ResetState();
        width?.ResetState();
        value?.ResetState();
        speed?.ResetState();
        density?.ResetState();
        count?.ResetState();

        if (angle != null) Angle = angle.Get();
        if (offset != null) Offset = offset.Get();
        if (margin != null) Margin = margin.Get();
        if (posX != null) PosX = posX.Get();
        if (posZ != null) PosZ = posZ.Get();
        if (direction != null) Direction = direction.Get();
        if (width != null) Width = width.Get();
        if (value != null) Value = value.Get();
        if (speed != null) Speed = speed.Get();
        if (density != null) Density = density.Get();
        if (count != null) Count = count.Get();
    }

    public override bool Calculate()
    {
        if (Mode == PlacementMode.Grid_Edge)
        {
            OutputKnob.SetValue<ISupplier<Path>>(new Output_GridEdge(
                SupplierOrFallback(AngleKnob, Angle),
                SupplierOrFallback(OffsetKnob, Offset),
                SupplierOrFallback(MarginKnob, Margin),
                SupplierOrFallback(DirectionKnob, Direction),
                SupplierOrFallback(WidthKnob, Width),
                SupplierOrFallback(ValueKnob, Value),
                SupplierOrFallback(SpeedKnob, Speed),
                SupplierOrFallback(DensityKnob, Density),
                SupplierOrFallback(CountKnob, Count)
            ));
        }
        else
        {
            OutputKnob.SetValue<ISupplier<Path>>(new Output_PositionXZ(
                SupplierOrFallback(PosXKnob, PosX),
                SupplierOrFallback(PosZKnob, PosZ),
                SupplierOrFallback(DirectionKnob, Direction),
                SupplierOrFallback(WidthKnob, Width),
                SupplierOrFallback(ValueKnob, Value),
                SupplierOrFallback(SpeedKnob, Speed),
                SupplierOrFallback(DensityKnob, Density),
                SupplierOrFallback(CountKnob, Count)
            ));
        }

        return true;
    }

    public enum PlacementMode
    {
        Position_XZ, Grid_Edge
    }

    private abstract class AbstractOutput : ISupplier<Path>
    {
        protected readonly ISupplier<double> Direction;
        protected readonly ISupplier<double> Width;
        protected readonly ISupplier<double> Value;
        protected readonly ISupplier<double> Speed;
        protected readonly ISupplier<double> Density;
        protected readonly ISupplier<double> Count;

        protected AbstractOutput(
            ISupplier<double> direction,
            ISupplier<double> width,
            ISupplier<double> value,
            ISupplier<double> speed,
            ISupplier<double> density,
            ISupplier<double> count)
        {
            Direction = direction;
            Width = width;
            Value = value;
            Speed = speed;
            Density = density;
            Count = count;
        }

        public abstract Path Get();

        protected void CreateOrigin(Path path, double posX, double posZ, double angle)
        {
            double direction = Direction.Get() + 180;
            double width = Width.Get().InRange(0, Path.MaxWidth);
            double value = Value.Get();
            double speed = Speed.Get();
            double density = Density.Get();

            var origin = path.AddOrigin(
                new Vector2d(posX, posZ),
                value, (angle + direction).NormalizeDeg(),
                width, speed, density
            );

            origin.AttachNewBranch();
        }

        public virtual void ResetState()
        {
            Direction.ResetState();
            Width.ResetState();
            Value.ResetState();
            Speed.ResetState();
            Density.ResetState();
            Count.ResetState();
        }
    }

    private class Output_PositionXZ : AbstractOutput
    {
        protected readonly ISupplier<double> PosX;
        protected readonly ISupplier<double> PosZ;

        public Output_PositionXZ(
            ISupplier<double> posX,
            ISupplier<double> posZ,
            ISupplier<double> direction,
            ISupplier<double> width,
            ISupplier<double> value,
            ISupplier<double> speed,
            ISupplier<double> density,
            ISupplier<double> count) :
            base(direction, width, value, speed, density, count)
        {
            PosX = posX;
            PosZ = posZ;
        }

        public override Path Get()
        {
            var count = Count.Get();

            var path = new Path();

            for (int i = 0; i < count; i++)
            {
                double posX = PosX.Get();
                double posZ = PosZ.Get();

                CreateOrigin(path, posX, posZ, 0);
            }

            return path;
        }

        public override void ResetState()
        {
            PosX.ResetState();
            PosZ.ResetState();
            base.ResetState();
        }
    }

    [HotSwappable]
    private class Output_GridEdge : AbstractOutput
    {
        protected readonly ISupplier<double> Angle;
        protected readonly ISupplier<double> Offset;
        protected readonly ISupplier<double> Margin;

        public Output_GridEdge(
            ISupplier<double> angle,
            ISupplier<double> offset,
            ISupplier<double> margin,
            ISupplier<double> direction,
            ISupplier<double> width,
            ISupplier<double> value,
            ISupplier<double> speed,
            ISupplier<double> density,
            ISupplier<double> count) :
            base(direction, width, value, speed, density, count)
        {
            Angle = angle;
            Offset = offset;
            Margin = margin;
        }

        public override Path Get()
        {
            var count = Count.Get();

            var path = new Path();

            for (int i = 0; i < count; i++)
            {
                var angle = Angle.Get() - 90;
                var offset = Offset.Get();
                var margin = Margin.Get();

                var vec = Vector2d.Direction(-angle);
                var pivot = new Vector2d(0.5, 0.5) + offset * vec.PerpCW;

                var divX = vec.x > 0 ? (1 - pivot.x) / vec.x : vec.x < 0 ? pivot.x / -vec.x : double.MaxValue;
                var divZ = vec.z > 0 ? (1 - pivot.z) / vec.z : vec.z < 0 ? pivot.z / -vec.z : double.MaxValue;

                var pos = pivot + vec * (Math.Min(divX, divZ) + margin);

                CreateOrigin(path, pos.x, pos.z, angle);
            }

            return path;
        }

        public override void ResetState()
        {
            Angle.ResetState();
            Offset.ResetState();
            Margin.ResetState();
            base.ResetState();
        }
    }
}
