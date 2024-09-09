using System;
using NodeEditorFramework;
using TerrainGraph.Flow;
using TerrainGraph.Util;
using UnityEngine;

namespace TerrainGraph;

[Serializable]
[Node(false, "Path/Origin", 600)]
public class NodePathOrigin : NodeBase
{
    public const string ID = "pathOrigin";
    public override string GetID => ID;

    public override string Title => "Path: Origin";

    [ValueConnectionKnob("Position x", NodeEditorFramework.Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob PosXKnob;

    [ValueConnectionKnob("Position z", NodeEditorFramework.Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob PosZKnob;

    [ValueConnectionKnob("Direction", NodeEditorFramework.Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob DirectionKnob;

    [ValueConnectionKnob("Width", NodeEditorFramework.Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob WidthKnob;

    [ValueConnectionKnob("Speed", NodeEditorFramework.Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob SpeedKnob;

    [ValueConnectionKnob("Density", NodeEditorFramework.Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob DensityKnob;

    [ValueConnectionKnob("Count", NodeEditorFramework.Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob CountKnob;

    [ValueConnectionKnob("Output", NodeEditorFramework.Direction.Out, PathFunctionConnection.Id)]
    public ValueConnectionKnob OutputKnob;

    public double PosX = 0.5;
    public double PosZ = 0.5;
    public double Direction;
    public double Width = 10;
    public double Speed = 1;
    public double Density = 1;
    public double Count = 1;

    public override void NodeGUI()
    {
        OutputKnob.SetPosition(FirstKnobPosition);

        GUILayout.BeginVertical(BoxStyle);

        KnobValueField(PosXKnob, ref PosX);
        KnobValueField(PosZKnob, ref PosZ);
        KnobValueField(DirectionKnob, ref Direction);
        KnobValueField(WidthKnob, ref Width);
        KnobValueField(SpeedKnob, ref Speed);
        KnobValueField(DensityKnob, ref Density);
        KnobValueField(CountKnob, ref Count);

        GUILayout.EndVertical();

        if (GUI.changed)
            canvas.OnNodeChange(this);
    }

    public override void RefreshPreview()
    {
        var posX = GetIfConnected<double>(PosXKnob);
        var posZ = GetIfConnected<double>(PosZKnob);
        var direction = GetIfConnected<double>(DirectionKnob);
        var width = GetIfConnected<double>(WidthKnob);
        var speed = GetIfConnected<double>(SpeedKnob);
        var density = GetIfConnected<double>(DensityKnob);
        var count = GetIfConnected<double>(CountKnob);

        foreach (var supplier in new[] { posX, posZ, direction, width, speed, density, count }) supplier?.ResetState();

        if (posX != null) PosX = posX.Get();
        if (posZ != null) PosZ = posZ.Get();
        if (direction != null) Direction = direction.Get();
        if (width != null) Width = width.Get();
        if (speed != null) Speed = speed.Get();
        if (density != null) Density = density.Get();
        if (count != null) Count = count.Get();
    }

    public override void CleanUpGUI()
    {
        if (PosXKnob.connected()) PosX = 0;
        if (PosZKnob.connected()) PosZ = 0;
        if (DirectionKnob.connected()) Direction = 0;
        if (WidthKnob.connected()) Width = 0;
        if (SpeedKnob.connected()) Speed = 0;
        if (DensityKnob.connected()) Density = 0;
        if (CountKnob.connected()) Count = 0;
    }

    public override bool Calculate()
    {
        OutputKnob.SetValue<ISupplier<Path>>(new Output(
            SupplierOrFallback(PosXKnob, PosX),
            SupplierOrFallback(PosZKnob, PosZ),
            SupplierOrFallback(DirectionKnob, Direction),
            SupplierOrFallback(WidthKnob, Width),
            SupplierOrFallback(SpeedKnob, Speed),
            SupplierOrFallback(DensityKnob, Density),
            SupplierOrFallback(CountKnob, Count),
            TerrainCanvas.GridPathSize
        ));

        return true;
    }

    private class Output : ISupplier<Path>
    {
        private readonly ISupplier<double> _posX;
        private readonly ISupplier<double> _posZ;
        private readonly ISupplier<double> _direction;
        private readonly ISupplier<double> _width;
        private readonly ISupplier<double> _speed;
        private readonly ISupplier<double> _density;
        private readonly ISupplier<double> _count;
        private readonly int _gridSize;

        public Output(
            ISupplier<double> posX, ISupplier<double> posZ,
            ISupplier<double> direction, ISupplier<double> width, ISupplier<double> speed,
            ISupplier<double> density, ISupplier<double> count, int gridSize)
        {
            _posX = posX;
            _posZ = posZ;
            _direction = direction;
            _width = width;
            _speed = speed;
            _density = density;
            _count = count;
            _gridSize = gridSize;
        }

        public Path Get()
        {
            var count = _count.Get();

            var path = new Path();

            for (int i = 0; i < count; i++)
            {
                double posX = _posX.Get();
                double posZ = _posZ.Get();

                CreateOrigin(path, posX, posZ, 0);
            }

            return path;
        }

        protected Path.Segment CreateOrigin(Path path, double posX, double posZ, double angle)
        {
            return new Path.Segment(path)
            {
                RelPosition = new Vector2d(posX, posZ) * _gridSize,
                RelAngle = (angle + _direction.Get() - 90).NormalizeDeg(),
                RelWidth = _width.Get().WithMin(0),
                RelSpeed = _speed.Get(),
                RelDensity = _density.Get()
            };
        }

        public virtual void ResetState()
        {
            _posX.ResetState();
            _posZ.ResetState();
            _direction.ResetState();
            _width.ResetState();
            _speed.ResetState();
            _density.ResetState();
            _count.ResetState();
        }
    }
}
