using System;
using System.Collections.Generic;
using System.Linq;
using NodeEditorFramework;
using NodeEditorFramework.Utilities;
using UnityEngine;
using static TerrainGraph.GridFunction;

namespace TerrainGraph;

[Serializable]
public abstract class NodeSelectBase : NodeBase
{
    public override string Title => Interpolated ? "Interpolate" : "Select";

    public abstract ValueConnectionKnob InputKnobRef { get; }
    public abstract ValueConnectionKnob OutputKnobRef { get; }

    public virtual bool SupportsInterpolation => false;

    [NonSerialized]
    public List<ValueConnectionKnob> OptionKnobs = [];

    public List<double> Thresholds = [];

    public bool Interpolated;

    public override void RefreshDynamicKnobs()
    {
        OptionKnobs = dynamicConnectionPorts.Where(k => k.name.StartsWith("Option")).Cast<ValueConnectionKnob>().ToList();
        UpdateThresholdArray();
    }

    private void UpdateThresholdArray()
    {
        while (Thresholds.Count < OptionKnobs.Count - 1) Thresholds.Add(Thresholds.Count == 0 ? 0 : Thresholds[Thresholds.Count - 1]);
        while (Thresholds.Count > 0 && Thresholds.Count > OptionKnobs.Count - 1) Thresholds.RemoveAt(Thresholds.Count - 1);
    }

    public override void NodeGUI()
    {
        OutputKnobRef.SetPosition(FirstKnobPosition);
        while (OptionKnobs.Count < 2) CreateNewOptionKnob();

        GUILayout.BeginVertical(BoxStyle);

        UpdateThresholdArray();

        GUILayout.BeginHorizontal(BoxStyle);
        GUILayout.Label("Input", BoxLayout);
        InputKnobRef.SetPosition();
        GUILayout.EndHorizontal();

        for (int i = 0; i < OptionKnobs.Count; i++)
        {
            var knob = OptionKnobs[i];
            GUILayout.BeginHorizontal(BoxStyle);

            DrawOption(knob, i);
            knob.SetPosition();

            if (i > 0)
            {
                GUILayout.FlexibleSpace();
                Thresholds[i - 1] = RTEditorGUI.FloatField(GUIContent.none, (float) Thresholds[i - 1], BoxLayout);
            }

            GUILayout.EndHorizontal();
        }

        GUILayout.EndVertical();

        if (GUI.changed)
            canvas.OnNodeChange(this);
    }

    protected abstract void DrawOption(ValueConnectionKnob knob, int i);

    protected abstract void CreateNewOptionKnob();

    public override void FillNodeActionsMenu(NodeEditorInputInfo inputInfo, GenericMenu menu)
    {
        base.FillNodeActionsMenu(inputInfo, menu);

        menu.AddSeparator("");

        if (!Interpolated && SupportsInterpolation)
        {
            menu.AddItem(new GUIContent("Add interpolation"), false, () =>
            {
                Interpolated = true;
                canvas.OnNodeChange(this);
            });
        }

        if (Interpolated)
        {
            menu.AddItem(new GUIContent("Remove interpolation"), false, () =>
            {
                Interpolated = false;
                canvas.OnNodeChange(this);
            });
        }

        menu.AddSeparator("");

        if (OptionKnobs.Count < 20)
        {
            menu.AddItem(new GUIContent("Add branch"), false, CreateNewOptionKnob);
        }

        if (OptionKnobs.Count > 2)
        {
            menu.AddItem(new GUIContent("Remove branch"), false, () =>
            {
                DeleteConnectionPort(OptionKnobs[OptionKnobs.Count - 1]);
                RefreshDynamicKnobs();
                canvas.OnNodeChange(this);
            });
        }
    }

    protected class Output<T> : ISupplier<T>
    {
        private readonly ISupplier<double> _input;
        private readonly List<ISupplier<T>> _options;
        private readonly List<double> _thresholds;
        private readonly Interpolation<T> _interpolation;

        public Output(
            ISupplier<double> input,
            List<ISupplier<T>> options,
            List<double> thresholds, Interpolation<T> interpolation)
        {
            _input = input;
            _options = options;
            _thresholds = thresholds;
            _interpolation = interpolation;
        }

        public T Get()
        {
            var value = _input.Get();

            for (int i = 0; i < Math.Min(_thresholds.Count, _options.Count - 1); i++)
            {
                if (value < _thresholds[i])
                {
                    if (_interpolation == null || i == 0) return _options[i].Get();
                    var t = (value - _thresholds[i - 1]) / (_thresholds[i] - _thresholds[i - 1]);
                    return _interpolation(t, _options[i].Get(), _options[i + 1].Get());
                }
            }

            return _options[_options.Count - 1].Get();
        }

        public void ResetState()
        {
            _input.ResetState();
            foreach (var option in _options) option.ResetState();
        }
    }

    protected class GridOutput<T> : ISupplier<IGridFunction<T>>
    {
        private readonly ISupplier<IGridFunction<double>> _input;
        private readonly List<ISupplier<IGridFunction<T>>> _options;
        private readonly List<double> _thresholds;
        private readonly Interpolation<T> _interpolation;
        private readonly Func<T, int, T> _postProcess;

        public GridOutput(
            ISupplier<IGridFunction<double>> input, List<ISupplier<IGridFunction<T>>> options,
            List<double> thresholds, Interpolation<T> interpolation, Func<T, int, T> postProcess = null)
        {
            _input = input;
            _options = options;
            _thresholds = thresholds;
            _interpolation = interpolation;
            _postProcess = postProcess;
        }

        public IGridFunction<T> Get()
        {
            return new Select<T>(
                _input.Get(), _options.Select(o => o.Get()).ToList(), _thresholds, _interpolation, _postProcess
            );
        }

        public void ResetState()
        {
            _input.ResetState();
            foreach (var option in _options) option.ResetState();
        }
    }
}
