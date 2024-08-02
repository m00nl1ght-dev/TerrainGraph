using System;
using System.Linq;
using NodeEditorFramework;
using TerrainGraph.Flow;
using TerrainGraph.Util;
using UnityEngine;

#pragma warning disable CS0659

namespace TerrainGraph;

[Serializable]
[Node(false, "Path/Width", 603)]
public class NodePathWidth : NodeBase
{
    public const string ID = "pathWidth";
    public override string GetID => ID;

    public override string Title => "Path: Width";

    [ValueConnectionKnob("Input", Direction.In, PathFunctionConnection.Id)]
    public ValueConnectionKnob InputKnob;

    [ValueConnectionKnob("Output", Direction.Out, PathFunctionConnection.Id)]
    public ValueConnectionKnob OutputKnob;

    public ValueConnectionKnob ByPositionKnob;
    public ValueConnectionKnob ByPatternKnob;
    public ValueConnectionKnob ByWidthKnob;
    public ValueConnectionKnob PatternScalingKnob;
    public ValueConnectionKnob SideBalanceKnob;

    public override void RefreshDynamicKnobs()
    {
        ByPositionKnob = FindOrCreateDynamicKnob(new("Extent ~ Position", Direction.In, GridFunctionConnection.Id));
        ByPatternKnob = FindOrCreateDynamicKnob(new("Extent ~ Pattern", Direction.In, CurveFunctionConnection.Id));
        ByWidthKnob = FindOrCreateDynamicKnob(new("Extent ~ Width", Direction.In, CurveFunctionConnection.Id));
        PatternScalingKnob = FindOrCreateDynamicKnob(new("Pattern ~ Stable width", Direction.In, CurveFunctionConnection.Id));
        SideBalanceKnob = FindOrCreateDynamicKnob(new("Side balance ~ Pattern", Direction.In, CurveFunctionConnection.Id));
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
        GUILayout.Label("Pattern Scaling", BoxLayout);
        GUILayout.EndHorizontal();

        PatternScalingKnob.SetPosition();

        GUILayout.BeginHorizontal(BoxStyle);
        GUILayout.Label("Side Balance", BoxLayout);
        GUILayout.EndHorizontal();

        SideBalanceKnob.SetPosition();

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
            GetIfConnected<ICurveFunction<double>>(PatternScalingKnob),
            GetIfConnected<ICurveFunction<double>>(SideBalanceKnob)
        ));
        return true;
    }

    private class Output : ISupplier<Path>
    {
        private readonly ISupplier<Path> _input;
        private readonly ISupplier<IGridFunction<double>> _byPosition;
        private readonly ISupplier<ICurveFunction<double>> _byPattern;
        private readonly ISupplier<ICurveFunction<double>> _byWidth;
        private readonly ISupplier<ICurveFunction<double>> _patternScaling;
        private readonly ISupplier<ICurveFunction<double>> _sideBalance;

        public Output(
            ISupplier<Path> input,
            ISupplier<IGridFunction<double>> byPosition,
            ISupplier<ICurveFunction<double>> byPattern,
            ISupplier<ICurveFunction<double>> byWidth,
            ISupplier<ICurveFunction<double>> patternScaling,
            ISupplier<ICurveFunction<double>> sideBalance)
        {
            _input = input;
            _byPosition = byPosition;
            _byPattern = byPattern;
            _byWidth = byWidth;
            _patternScaling = patternScaling;
            _sideBalance = sideBalance;
        }

        public Path Get()
        {
            var path = new Path(_input.Get());

            foreach (var segment in path.Leaves.ToList())
            {
                var extParams = segment.TraceParams;

                extParams.ExtentLeft = new ParamFunc(
                    _byPosition?.Get(),
                    _byPattern?.Get(),
                    _byWidth?.Get(),
                    _patternScaling?.Get(),
                    _sideBalance?.Get(),
                    true
                );

                extParams.ExtentRight = new ParamFunc(
                    _byPosition?.Get(),
                    _byPattern?.Get(),
                    _byWidth?.Get(),
                    _patternScaling?.Get(),
                    _sideBalance?.Get(),
                    false
                );

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
            _patternScaling?.ResetState();
            _sideBalance?.ResetState();
        }
    }

    private class ParamFunc : TraceParamFunction
    {
        private readonly IGridFunction<double> _byPosition;
        private readonly ICurveFunction<double> _byPattern;
        private readonly ICurveFunction<double> _byWidth;
        private readonly ICurveFunction<double> _patternScaling;
        private readonly ICurveFunction<double> _sideBalance;
        private readonly bool _leftSide;

        public ParamFunc(
            IGridFunction<double> byPosition,
            ICurveFunction<double> byPattern,
            ICurveFunction<double> byWidth,
            ICurveFunction<double> patternScaling,
            ICurveFunction<double> sideBalance,
            bool leftSide)
        {
            _byPosition = byPosition;
            _byPattern = byPattern;
            _byWidth = byWidth;
            _patternScaling = patternScaling;
            _sideBalance = sideBalance;
            _leftSide = leftSide;
        }

        public override double ValueFor(PathTracer tracer, TraceTask task, Vector2d pos, double dist)
        {
            var value = 1d;

            var offset = task.branchParent.segment.Id * 37 % 100 * 10;
            var scaling = _patternScaling?.ValueAt(task.branchParent.WidthAt(0)) ?? 1;

            if (_byPosition != null)
                value *= _byPosition.ValueAt(pos);

            if (_byPattern != null)
                value *= _byPattern.ValueAt(offset + scaling * (task.distFromRoot + dist));

            if (_sideBalance != null)
                value += _sideBalance.ValueAt(offset + scaling * (task.distFromRoot + dist)) * (_leftSide ? -1 : 1);

            if (_byWidth != null)
                value = value.ScaleAround(1, _byWidth.ValueAt(task.WidthAt(dist)));

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
            Equals(_patternScaling, other._patternScaling) &&
            Equals(_sideBalance, other._sideBalance) &&
            _leftSide == other._leftSide;

        public override string ToString() =>
            $"Position ~ {_byPosition}, " +
            $"Width ~ {_byWidth}, " +
            $"Pattern ~ {_byPattern} " +
            $"scaled by {_patternScaling} " +
            $"with side balance {_sideBalance} @ " +
            (_leftSide ? "left" : "right");
    }
}
