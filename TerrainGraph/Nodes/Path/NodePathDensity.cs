using System;
using System.Linq;
using NodeEditorFramework;
using TerrainGraph.Flow;
using TerrainGraph.Util;
using UnityEngine;

#pragma warning disable CS0659

namespace TerrainGraph;

[Serializable]
[Node(false, "Path/Density", 605)]
public class NodePathDensity : NodeBase
{
    public const string ID = "pathDensity";
    public override string GetID => ID;

    public override string Title => "Path: Density";

    [ValueConnectionKnob("Input", Direction.In, PathFunctionConnection.Id)]
    public ValueConnectionKnob InputKnob;

    [ValueConnectionKnob("Density loss", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob DensityLossKnob;

    [ValueConnectionKnob("Output", Direction.Out, PathFunctionConnection.Id)]
    public ValueConnectionKnob OutputKnob;

    public ValueConnectionKnob ByPositionKnob;
    public ValueConnectionKnob ByExtentKnob;

    public double DensityLoss;

    public override void RefreshDynamicKnobs()
    {
        ByPositionKnob = FindOrCreateDynamicKnob(new("Density ~ Position", Direction.In, GridFunctionConnection.Id));
        ByExtentKnob = FindOrCreateDynamicKnob(new("Density ~ Extent", Direction.In, CurveFunctionConnection.Id));
    }

    public override void NodeGUI()
    {
        InputKnob.SetPosition(FirstKnobPosition);
        OutputKnob.SetPosition(FirstKnobPosition);

        GUILayout.BeginVertical(BoxStyle);

        GUILayout.BeginHorizontal(BoxStyle);
        GUILayout.Label("Input", BoxLayout);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal(BoxStyle);
        GUILayout.Label("~ Position", BoxLayout);
        GUILayout.EndHorizontal();

        ByPositionKnob.SetPosition();

        GUILayout.BeginHorizontal(BoxStyle);
        GUILayout.Label("~ Extent", BoxLayout);
        GUILayout.EndHorizontal();

        ByExtentKnob.SetPosition();

        KnobValueField(DensityLossKnob, ref DensityLoss);

        GUILayout.EndVertical();

        if (GUI.changed)
            canvas.OnNodeChange(this);
    }

    public override void RefreshPreview()
    {
        var densityLoss = GetIfConnected<double>(DensityLossKnob);

        densityLoss?.ResetState();

        if (densityLoss != null) DensityLoss = densityLoss.Get();
    }

    public override bool Calculate()
    {
        OutputKnob.SetValue<ISupplier<Path>>(new Output(
            SupplierOrFallback(InputKnob, Path.Empty),
            GetIfConnected<IGridFunction<double>>(ByPositionKnob),
            GetIfConnected<ICurveFunction<double>>(ByExtentKnob),
            SupplierOrFallback(DensityLossKnob, DensityLoss)
        ));
        return true;
    }

    private class Output : ISupplier<Path>
    {
        private readonly ISupplier<Path> _input;
        private readonly ISupplier<IGridFunction<double>> _byPosition;
        private readonly ISupplier<ICurveFunction<double>> _byExtent;
        private readonly ISupplier<double> _densityLoss;

        public Output(
            ISupplier<Path> input,
            ISupplier<IGridFunction<double>> byPosition,
            ISupplier<ICurveFunction<double>> byExtent,
            ISupplier<double> densityLoss)
        {
            _input = input;
            _byPosition = byPosition;
            _byExtent = byExtent;
            _densityLoss = densityLoss;
        }

        public Path Get()
        {
            var path = new Path(_input.Get());

            foreach (var segment in path.Leaves.ToList())
            {
                var extParams = segment.TraceParams;

                extParams.DensityLeft = new ParamFunc(_byPosition?.Get(), _byExtent?.Get(), true);
                extParams.DensityRight = new ParamFunc(_byPosition?.Get(), _byExtent?.Get(), false);
                extParams.DensityLoss = _densityLoss.Get();

                segment.ExtendWithParams(extParams);
            }

            return path;
        }

        public void ResetState()
        {
            _input.ResetState();
            _byPosition?.ResetState();
            _byExtent?.ResetState();
            _densityLoss.ResetState();
        }
    }

    private class ParamFunc : TraceParamFunction
    {
        private readonly IGridFunction<double> _byPosition;
        private readonly ICurveFunction<double> _byExtent;
        private readonly bool _leftSide;

        public ParamFunc(
            IGridFunction<double> byPosition,
            ICurveFunction<double> byExtent,
            bool leftSide)
        {
            _byPosition = byPosition;
            _byExtent = byExtent;
            _leftSide = leftSide;
        }

        public override double ValueFor(PathTracer tracer, TraceTask task, Vector2d pos, double dist)
        {
            var value = 1d;

            if (_byPosition != null)
                value *= _byPosition.ValueAt(pos);

            if (_byExtent != null)
                if (_leftSide)
                    value *= _byExtent.ValueAt(task.segment.TraceParams.ExtentLeft?.ValueFor(tracer, task, pos, dist) ?? 1);
                else
                    value *= _byExtent.ValueAt(task.segment.TraceParams.ExtentRight?.ValueFor(tracer, task, pos, dist) ?? 1);

            return value;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((ParamFunc) obj);
        }

        protected bool Equals(ParamFunc other) =>
            Equals(_byPosition, other._byPosition) &&
            Equals(_byExtent, other._byExtent) &&
            _leftSide == other._leftSide;

        public override string ToString() =>
            $"Position ~ {_byPosition}, " +
            $"Extent ~ {_byExtent} @ " +
            (_leftSide ? "left" : "right");
    }
}
