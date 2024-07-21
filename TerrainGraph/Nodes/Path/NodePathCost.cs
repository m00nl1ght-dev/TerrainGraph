using System;
using System.Linq;
using NodeEditorFramework;
using TerrainGraph.Flow;
using TerrainGraph.Util;
using UnityEngine;

#pragma warning disable CS0659

namespace TerrainGraph;

[Serializable]
[Node(false, "Path/Cost", 607)]
public class NodePathCost : NodeBase
{
    public const string ID = "pathCost";
    public override string GetID => ID;

    public override string Title => "Path: Cost";

    [ValueConnectionKnob("Input", Direction.In, PathFunctionConnection.Id)]
    public ValueConnectionKnob InputKnob;

    [ValueConnectionKnob("Output", Direction.Out, PathFunctionConnection.Id)]
    public ValueConnectionKnob OutputKnob;

    public ValueConnectionKnob ByPositionKnob;
    public ValueConnectionKnob ByOverlapKnob;

    public override void RefreshDynamicKnobs()
    {
        ByPositionKnob = FindOrCreateDynamicKnob(new("Cost ~ Position", Direction.In, GridFunctionConnection.Id));
        ByOverlapKnob = FindOrCreateDynamicKnob(new("Cost ~ Overlap", Direction.In, CurveFunctionConnection.Id));
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
        GUILayout.Label("~ Overlap", BoxLayout);
        GUILayout.EndHorizontal();

        ByOverlapKnob.SetPosition();

        GUILayout.EndVertical();

        if (GUI.changed)
            canvas.OnNodeChange(this);
    }

    public override bool Calculate()
    {
        OutputKnob.SetValue<ISupplier<Path>>(new Output(
            SupplierOrFallback(InputKnob, Path.Empty),
            GetIfConnected<IGridFunction<double>>(ByPositionKnob),
            GetIfConnected<ICurveFunction<double>>(ByOverlapKnob)
        ));
        return true;
    }

    private class Output : ISupplier<Path>
    {
        private readonly ISupplier<Path> _input;
        private readonly ISupplier<IGridFunction<double>> _byPosition;
        private readonly ISupplier<ICurveFunction<double>> _byOverlap;

        public Output(
            ISupplier<Path> input,
            ISupplier<IGridFunction<double>> byPosition,
            ISupplier<ICurveFunction<double>> byOverlap)
        {
            _input = input;
            _byPosition = byPosition;
            _byOverlap = byOverlap;
        }

        public Path Get()
        {
            var path = new Path(_input.Get());

            foreach (var segment in path.Leaves.ToList())
            {
                var extParams = segment.TraceParams;

                extParams.Cost = _byPosition != null || _byOverlap != null ?
                    new ParamFunc(_byPosition?.Get(), _byOverlap?.Get()) : null;

                extParams.PreventPathMerge = _byOverlap != null;

                segment.ExtendWithParams(extParams);
            }

            return path;
        }

        public void ResetState()
        {
            _input.ResetState();
            _byPosition?.ResetState();
            _byOverlap?.ResetState();
        }
    }

    private class ParamFunc : TraceParamFunction
    {
        private readonly IGridFunction<double> _byPosition;
        private readonly ICurveFunction<double> _byOverlap;

        public ParamFunc(
            IGridFunction<double> byPosition,
            ICurveFunction<double> byOverlap)
        {
            _byPosition = byPosition;
            _byOverlap = byOverlap;
        }

        public override double ValueFor(PathTracer tracer, TraceTask task, Vector2d pos, double dist)
        {
            var value = 0d;

            if (_byPosition != null)
                value += _byPosition.ValueAt(pos);

            if (_byOverlap != null && dist > 0)
            {
                var factor = task.segment.TraceParams.Target != null ? 100 : 10;
                var input = tracer.DistanceGrid.ValueAt(pos) - task.WidthAt(dist) / 2;
                value += factor * _byOverlap.ValueAt(input);
            }

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
            Equals(_byOverlap, other._byOverlap);

        public override string ToString() =>
            $"Position ~ {_byPosition}, " +
            $"Overlap ~ {_byOverlap}";
    }
}
