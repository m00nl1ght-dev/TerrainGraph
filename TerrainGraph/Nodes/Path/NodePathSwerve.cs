using System;
using System.Linq;
using NodeEditorFramework;
using TerrainGraph.Flow;
using TerrainGraph.Util;
using UnityEngine;

#pragma warning disable CS0659

namespace TerrainGraph;

[Serializable]
[Node(false, "Path/Swerve", 605)]
public class NodePathSwerve : NodeBase
{
    public const string ID = "pathSwerve";
    public override string GetID => ID;

    public override string Title => "Path: Swerve";

    [ValueConnectionKnob("Input", Direction.In, PathFunctionConnection.Id)]
    public ValueConnectionKnob InputKnob;

    [ValueConnectionKnob("Output", Direction.Out, PathFunctionConnection.Id)]
    public ValueConnectionKnob OutputKnob;

    public ValueConnectionKnob ByPositionKnob;
    public ValueConnectionKnob ByDistanceKnob;
    public ValueConnectionKnob ByCostKnob;

    public override void RefreshDynamicKnobs()
    {
        ByPositionKnob = FindOrCreateDynamicKnob(new("Swerve ~ Position", Direction.In, GridFunctionConnection.Id));
        ByDistanceKnob = FindOrCreateDynamicKnob(new("Swerve ~ Distance", Direction.In, CurveFunctionConnection.Id));
        ByCostKnob = FindOrCreateDynamicKnob(new("Swerve ~ Cost", Direction.In, CurveFunctionConnection.Id));
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
        GUILayout.Label("~ Distance", BoxLayout);
        GUILayout.EndHorizontal();

        ByDistanceKnob.SetPosition();

        GUILayout.BeginHorizontal(BoxStyle);
        GUILayout.Label("~ Cost", BoxLayout);
        GUILayout.EndHorizontal();

        ByCostKnob.SetPosition();

        GUILayout.EndVertical();

        if (GUI.changed)
            canvas.OnNodeChange(this);
    }

    public override bool Calculate()
    {
        OutputKnob.SetValue<ISupplier<Path>>(new Output(
            SupplierOrFallback(InputKnob, Path.Empty),
            GetIfConnected<IGridFunction<double>>(ByPositionKnob),
            GetIfConnected<ICurveFunction<double>>(ByDistanceKnob),
            GetIfConnected<ICurveFunction<double>>(ByCostKnob)
        ));
        return true;
    }

    private class Output : ISupplier<Path>
    {
        private readonly ISupplier<Path> _input;
        private readonly ISupplier<IGridFunction<double>> _byPosition;
        private readonly ISupplier<ICurveFunction<double>> _byDistance;
        private readonly ISupplier<ICurveFunction<double>> _byCost;

        public Output(
            ISupplier<Path> input,
            ISupplier<IGridFunction<double>> byPosition,
            ISupplier<ICurveFunction<double>> byDistance,
            ISupplier<ICurveFunction<double>> byCost)
        {
            _input = input;
            _byPosition = byPosition;
            _byDistance = byDistance;
            _byCost = byCost;
        }

        public Path Get()
        {
            var path = new Path(_input.Get());

            var anySuppliers = _byPosition != null || _byDistance != null || _byCost != null;

            foreach (var segment in path.Leaves.ToList())
            {
                var extParams = segment.TraceParams;

                if (anySuppliers)
                {
                    extParams.Swerve = new ParamFunc(
                        _byPosition?.Get(),
                        _byDistance?.Get(),
                        _byCost?.Get()
                    );
                }
                else
                {
                    extParams.Swerve = null;
                }

                segment.ExtendWithParams(extParams);
            }

            return path;
        }

        public void ResetState()
        {
            _input.ResetState();
            _byPosition?.ResetState();
            _byDistance?.ResetState();
            _byCost?.ResetState();
        }
    }

    private class ParamFunc : TraceParamFunction
    {
        private readonly IGridFunction<double> _byPosition;
        private readonly ICurveFunction<double> _byDistance;
        private readonly ICurveFunction<double> _byCost;

        public ParamFunc(
            IGridFunction<double> byPosition,
            ICurveFunction<double> byDistance,
            ICurveFunction<double> byCost)
        {
            _byPosition = byPosition;
            _byDistance = byDistance;
            _byCost = byCost;
        }

        public override double ValueFor(PathTracer tracer, TraceTask task, Vector2d pos, double dist)
        {
            var value = 1d;

            if (_byPosition != null)
                value *= _byPosition.ValueAt(pos);

            if (_byDistance != null)
                value *= _byDistance.ValueAt(task.distFromRoot + dist);

            if (_byCost != null)
                value *= _byCost.ValueAt(task.segment.TraceParams.Cost?.ValueFor(tracer, task, pos, dist) ?? 0);

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
            Equals(_byDistance, other._byDistance) &&
            Equals(_byCost, other._byCost);

        public override string ToString() =>
            $"Position ~ {_byPosition}, " +
            $"Distance ~ {_byDistance}, " +
            $"Cost ~ {_byCost}";
    }
}
