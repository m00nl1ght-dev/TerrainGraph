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
    public ValueConnectionKnob ByTotalDistKnob;
    public ValueConnectionKnob ByPathCostKnob;

    public override void RefreshDynamicKnobs()
    {
        ByPositionKnob = FindOrCreateDynamicKnob(new("Swerve ~ Position", Direction.In, GridFunctionConnection.Id));
        ByTotalDistKnob = FindOrCreateDynamicKnob(new("Swerve ~ Total distance", Direction.In, CurveFunctionConnection.Id));
        ByPathCostKnob = FindOrCreateDynamicKnob(new("Swerve ~ Path cost", Direction.In, CurveFunctionConnection.Id));
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

        ByTotalDistKnob.SetPosition();

        GUILayout.BeginHorizontal(BoxStyle);
        GUILayout.Label("~ Cost", BoxLayout);
        GUILayout.EndHorizontal();

        ByPathCostKnob.SetPosition();

        GUILayout.EndVertical();

        if (GUI.changed)
            canvas.OnNodeChange(this);
    }

    public override bool Calculate()
    {
        OutputKnob.SetValue<ISupplier<Path>>(new Output(
            SupplierOrFallback(InputKnob, Path.Empty),
            GetIfConnected<IGridFunction<double>>(ByPositionKnob),
            GetIfConnected<ICurveFunction<double>>(ByTotalDistKnob),
            GetIfConnected<ICurveFunction<double>>(ByPathCostKnob)
        ));
        return true;
    }

    private class Output : ISupplier<Path>
    {
        private readonly ISupplier<Path> _input;
        private readonly ISupplier<IGridFunction<double>> _byPosition;
        private readonly ISupplier<ICurveFunction<double>> _byTotalDist;
        private readonly ISupplier<ICurveFunction<double>> _byPathCost;

        public Output(
            ISupplier<Path> input,
            ISupplier<IGridFunction<double>> byPosition,
            ISupplier<ICurveFunction<double>> byTotalDist,
            ISupplier<ICurveFunction<double>> byPathCost)
        {
            _input = input;
            _byPosition = byPosition;
            _byTotalDist = byTotalDist;
            _byPathCost = byPathCost;
        }

        public Path Get()
        {
            var path = new Path(_input.Get());

            var anySuppliers = _byPosition != null || _byTotalDist != null || _byPathCost != null;

            foreach (var segment in path.Leaves.ToList())
            {
                var extParams = segment.TraceParams;

                if (anySuppliers)
                {
                    extParams.Swerve = new ParamFunc(
                        _byPosition?.Get(),
                        _byTotalDist?.Get(),
                        _byPathCost?.Get()
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
            _byPosition.ResetState();
            _byTotalDist.ResetState();
            _byPathCost.ResetState();
        }
    }

    private class ParamFunc : TraceParamFunction
    {
        private readonly IGridFunction<double> _byPosition;
        private readonly ICurveFunction<double> _byTotalDist;
        private readonly ICurveFunction<double> _byPathCost;

        public ParamFunc(
            IGridFunction<double> byPosition,
            ICurveFunction<double> byTotalDist,
            ICurveFunction<double> byPathCost)
        {
            _byPosition = byPosition;
            _byTotalDist = byTotalDist;
            _byPathCost = byPathCost;
        }

        public override double ValueFor(TraceTask task, Vector2d pos, double dist)
        {
            var value = 1d;

            if (_byPosition != null)
                value *= _byPosition.ValueAt(pos);

            if (_byTotalDist != null)
                value *= _byTotalDist.ValueAt(task.distFromRoot + dist);

            if (_byPathCost != null)
                value *= _byPathCost.ValueAt(task.segment.TraceParams.Cost?.ValueFor(task, pos, dist) ?? 0);

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
            Equals(_byTotalDist, other._byTotalDist) &&
            Equals(_byPathCost, other._byPathCost);

        public override string ToString() =>
            $"Position ~ {_byPosition}, " +
            $"TotalDist ~ {_byTotalDist}, " +
            $"PathCost ~ {_byPathCost}";
    }
}
