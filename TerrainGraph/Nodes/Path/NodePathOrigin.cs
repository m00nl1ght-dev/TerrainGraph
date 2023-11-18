using System;
using NodeEditorFramework;
using TerrainGraph.Util;
using UnityEngine;

namespace TerrainGraph;

[Serializable]
[Node(false, "Path/Origin", 601)]
public class NodePathOrigin : NodeBase
{
    public const string ID = "pathOrigin";
    public override string GetID => ID;

    public override string Title => "Path: Origin";

    [ValueConnectionKnob("Angle", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob AngleKnob;
    
    [ValueConnectionKnob("Angle Offset", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob AngleOffsetKnob;

    [ValueConnectionKnob("Centrality", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob CentralityKnob;
    
    [ValueConnectionKnob("Width", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob WidthKnob;
    
    [ValueConnectionKnob("Count", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob CountKnob;

    [ValueConnectionKnob("Output", Direction.Out, PathFunctionConnection.Id)]
    public ValueConnectionKnob OutputKnob;

    public double Angle;
    public double AngleOffset;
    public double Centrality;
    public double Width = 10;
    public double Count = 1;

    public override void NodeGUI()
    {
        OutputKnob.SetPosition(FirstKnobPosition);

        GUILayout.BeginVertical(BoxStyle);

        KnobValueField(AngleKnob, ref Angle);
        KnobValueField(AngleOffsetKnob, ref AngleOffset);
        KnobValueField(CentralityKnob, ref Centrality);
        KnobValueField(WidthKnob, ref Width);
        KnobValueField(CountKnob, ref Count);

        GUILayout.EndVertical();

        if (GUI.changed)
            canvas.OnNodeChange(this);
    }

    public override void RefreshPreview()
    {
        var angle = GetIfConnected<double>(AngleKnob);
        var angleOffset = GetIfConnected<double>(AngleOffsetKnob);
        var centrality = GetIfConnected<double>(CentralityKnob);
        var width = GetIfConnected<double>(WidthKnob);
        var count = GetIfConnected<double>(CountKnob);
        
        angle?.ResetState();
        angleOffset?.ResetState();
        centrality?.ResetState();
        width?.ResetState();
        count?.ResetState();

        if (angle != null) Angle = angle.Get();
        if (angleOffset != null) AngleOffset = angleOffset.Get();
        if (centrality != null) Centrality = centrality.Get();
        if (width != null) Width = width.Get();
        if (count != null) Count = count.Get();
    }

    public override bool Calculate()
    {
        OutputKnob.SetValue<ISupplier<Path>>(new Output(
            SupplierOrValueFixed(AngleKnob, Angle),
            SupplierOrValueFixed(AngleOffsetKnob, AngleOffset),
            SupplierOrValueFixed(CentralityKnob, Centrality),
            SupplierOrValueFixed(WidthKnob, Width),
            SupplierOrValueFixed(CountKnob, Count),
            GridSize
        ));
        return true;
    }

    private class Output : ISupplier<Path>
    {
        private readonly ISupplier<double> _angle;
        private readonly ISupplier<double> _angleOffset;
        private readonly ISupplier<double> _centrality;
        private readonly ISupplier<double> _width;
        private readonly ISupplier<double> _count;
        private readonly double _gridSize;

        public Output(
            ISupplier<double> angle, 
            ISupplier<double> angleOffset, 
            ISupplier<double> centrality, 
            ISupplier<double> width, 
            ISupplier<double> count, 
            double gridSize)
        {
            _angle = angle;
            _angleOffset = angleOffset;
            _centrality = centrality;
            _width = width;
            _count = count;
            _gridSize = gridSize;
            _width = width;
        }

        public Path Get()
        {
            double count = _count.Get();

            var path = new Path();

            for (int i = 0; i < count; i++)
            {
                double angle = _angle.Get();
                double angleOffset = _angleOffset.Get();
                double centrality = _centrality.Get().InRange01();
                double width = _width.Get().InRange(0, Path.MaxWidth);

                double r = 0.5 * (1 - centrality);
                double x = 0.5 + r * Math.Cos(angle.ToRad());
                double z = 0.5 + r * Math.Sin(angle.ToRad());
                
                path.AddOrigin(
                    x.InRange01(), 
                    z.InRange01(), 
                    (angle + angleOffset).NormalizeDeg(),
                    width
                );
            }

            return path;
        }

        public void ResetState()
        {
            _angle.ResetState();
            _angleOffset.ResetState();
            _centrality.ResetState();
            _width.ResetState();
            _count.ResetState();
        }
    }
}