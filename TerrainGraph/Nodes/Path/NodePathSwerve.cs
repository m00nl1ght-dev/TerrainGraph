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
    public ValueConnectionKnob ByPatternKnob;
    public ValueConnectionKnob ByWidthKnob;
    public ValueConnectionKnob ByCostKnob;
    public ValueConnectionKnob PatternScalingKnob;

    public override void RefreshDynamicKnobs()
    {
        ByPositionKnob = FindOrCreateDynamicKnob(new("Swerve ~ Position", Direction.In, GridFunctionConnection.Id));
        ByPatternKnob = FindOrCreateDynamicKnob(new("Swerve ~ Pattern", Direction.In, CurveFunctionConnection.Id));
        ByWidthKnob = FindOrCreateDynamicKnob(new("Swerve ~ Width", Direction.In, CurveFunctionConnection.Id));
        ByCostKnob = FindOrCreateDynamicKnob(new("Swerve ~ Cost", Direction.In, CurveFunctionConnection.Id));
        PatternScalingKnob = FindOrCreateDynamicKnob(new("Pattern ~ Stable width", Direction.In, CurveFunctionConnection.Id));
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
        GUILayout.Label("~ Pattern", BoxLayout);
        GUILayout.EndHorizontal();

        ByPatternKnob.SetPosition();

        GUILayout.BeginHorizontal(BoxStyle);
        GUILayout.Label("~ Width", BoxLayout);
        GUILayout.EndHorizontal();

        ByWidthKnob.SetPosition();

        GUILayout.BeginHorizontal(BoxStyle);
        GUILayout.Label("~ Cost", BoxLayout);
        GUILayout.EndHorizontal();

        ByCostKnob.SetPosition();

        GUILayout.BeginHorizontal(BoxStyle);
        GUILayout.Label("Pattern Scaling", BoxLayout);
        GUILayout.EndHorizontal();

        PatternScalingKnob.SetPosition();

        GUILayout.EndVertical();

        if (GUI.changed)
            canvas.OnNodeChange(this);
    }

    public override bool Calculate()
    {
        OutputKnob.SetValue<ISupplier<Path>>(new Output(
            SupplierOrFallback(InputKnob, Path.Empty),
            GetIfConnected<IGridFunction<double>>(ByPositionKnob),
            GetIfConnected<ICurveFunction<double>>(ByPatternKnob),
            GetIfConnected<ICurveFunction<double>>(ByWidthKnob),
            GetIfConnected<ICurveFunction<double>>(ByCostKnob),
            GetIfConnected<ICurveFunction<double>>(PatternScalingKnob),
            TerrainCanvas.GridFullSize / (double) TerrainCanvas.GridPathSize
        ));
        return true;
    }

    private class Output : ISupplier<Path>
    {
        private readonly ISupplier<Path> _input;
        private readonly ISupplier<IGridFunction<double>> _byPosition;
        private readonly ISupplier<ICurveFunction<double>> _byPattern;
        private readonly ISupplier<ICurveFunction<double>> _byWidth;
        private readonly ISupplier<ICurveFunction<double>> _byCost;
        private readonly ISupplier<ICurveFunction<double>> _patternScaling;
        private readonly double _gridScale;

        public Output(
            ISupplier<Path> input,
            ISupplier<IGridFunction<double>> byPosition,
            ISupplier<ICurveFunction<double>> byPattern,
            ISupplier<ICurveFunction<double>> byWidth,
            ISupplier<ICurveFunction<double>> byCost,
            ISupplier<ICurveFunction<double>> patternScaling,
            double gridScale)
        {
            _input = input;
            _byPosition = byPosition;
            _byPattern = byPattern;
            _byWidth = byWidth;
            _byCost = byCost;
            _patternScaling = patternScaling;
            _gridScale = gridScale;
        }

        public Path Get()
        {
            var path = new Path(_input.Get());

            var anySuppliers = _byPosition != null || _byPattern != null;

            foreach (var segment in path.Leaves.ToList())
            {
                var extParams = segment.TraceParams;

                if (anySuppliers)
                {
                    extParams.Swerve = new ParamFunc(
                        _byPosition?.Get().Scaled(_gridScale),
                        _byPattern?.Get(),
                        _byWidth?.Get(),
                        _byCost?.Get(),
                        _patternScaling?.Get()
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
            _byPattern?.ResetState();
            _byWidth?.ResetState();
            _byCost?.ResetState();
            _patternScaling?.ResetState();
        }
    }

    private class ParamFunc : TraceParamFunction
    {
        private readonly IGridFunction<double> _byPosition;
        private readonly ICurveFunction<double> _byPattern;
        private readonly ICurveFunction<double> _byWidth;
        private readonly ICurveFunction<double> _byCost;
        private readonly ICurveFunction<double> _patternScaling;

        public ParamFunc(
            IGridFunction<double> byPosition,
            ICurveFunction<double> byPattern,
            ICurveFunction<double> byWidth,
            ICurveFunction<double> byCost,
            ICurveFunction<double> patternScaling)
        {
            _byPosition = byPosition;
            _byPattern = byPattern;
            _byWidth = byWidth;
            _byCost = byCost;
            _patternScaling = patternScaling;
        }

        public override double ValueFor(PathTracer tracer, TraceTask task, Vector2d pos, double dist, double stability)
        {
            var value = 1d;

            var offset = task.branchParent.segment.Id * 37 % 100 * 10;
            var scaling = _patternScaling?.ValueAt(task.branchParent.WidthAt(0)) ?? 1;

            if (_byPosition != null)
                value *= _byPosition.ValueAt(pos);

            if (_byPattern != null)
                value *= _byPattern.ValueAt(offset + scaling * (task.distFromRoot + dist));

            if (_byWidth != null)
                value *= _byWidth.ValueAt(task.WidthAt(dist));

            if (_byCost != null)
                value *= _byCost.ValueAt(task.segment.TraceParams.Cost?.ValueFor(tracer, task, pos, dist, stability) ?? 0);

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
            Equals(_byPattern, other._byPattern) &&
            Equals(_byWidth, other._byWidth) &&
            Equals(_byCost, other._byCost) &&
            Equals(_patternScaling, other._patternScaling);

        public override string ToString() =>
            $"Position ~ {_byPosition}, " +
            $"Width ~ {_byWidth}, " +
            $"Cost ~ {_byCost}" +
            $"Pattern ~ {_byPattern} " +
            $"scaled by {_patternScaling} ";

    }
}
