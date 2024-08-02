using System;
using System.Collections.Generic;
using System.Linq;
using NodeEditorFramework;
using NodeEditorFramework.Utilities;
using UnityEngine;
using static TerrainGraph.Util.MathUtil;

namespace TerrainGraph;

[Serializable]
public abstract class NodeSelectBase<TKey, TVal> : NodeBase
{
    public override string Title => Interpolated ? "Interpolate" : "Select";

    public abstract ValueConnectionKnob InputKnobRef { get; }
    public abstract ValueConnectionKnob OutputKnobRef { get; }

    protected abstract string OptionConnectionTypeId { get; }

    public virtual bool SupportsInterpolation => false;

    [NonSerialized]
    public List<ValueConnectionKnob> OptionKnobs = [];

    public List<TKey> Thresholds = [];
    public List<TVal> Values = [];

    public bool Interpolated;

    public override void RefreshDynamicKnobs()
    {
        OptionKnobs = dynamicConnectionPorts.Where(k => k.name.StartsWith("Option")).Cast<ValueConnectionKnob>().ToList();

        var expectedKeyCount = Interpolated ? OptionKnobs.Count : OptionKnobs.Count - 1;

        while (Thresholds.Count < expectedKeyCount) Thresholds.Add(Thresholds.Count == 0 ? default : Thresholds[Thresholds.Count - 1]);
        while (Thresholds.Count > 0 && Thresholds.Count > expectedKeyCount) Thresholds.RemoveAt(Thresholds.Count - 1);

        while (Values.Count < OptionKnobs.Count) Values.Add(default);
        while (Values.Count > OptionKnobs.Count) Values.RemoveAt(Values.Count - 1);
    }

    public override void OnCreate(bool fromGUI)
    {
        base.OnCreate(fromGUI);

        if (fromGUI)
        {
            CreateNewOptionKnob();
            CreateNewOptionKnob();
            RefreshDynamicKnobs();
        }
    }

    private void CreateNewOptionKnob()
    {
        CreateValueConnectionKnob(new("Option " + OptionKnobs.Count, Direction.In, OptionConnectionTypeId));
    }

    public override void NodeGUI()
    {
        OutputKnobRef.SetPosition(FirstKnobPosition);

        GUILayout.BeginVertical(BoxStyle);

        GUILayout.BeginHorizontal(BoxStyle);
        GUILayout.Label("Input", BoxLayout);
        InputKnobRef.SetPosition();
        GUILayout.EndHorizontal();

        for (int i = 0; i < OptionKnobs.Count; i++)
        {
            var knob = OptionKnobs[i];
            GUILayout.BeginHorizontal(BoxStyle);

            DrawOptionValue(i);
            knob.SetPosition();

            if (i > 0 || Interpolated)
            {
                GUILayout.FlexibleSpace();
                var tIdx = Interpolated ? i : i - 1;
                DrawOptionKey(tIdx);
            }

            GUILayout.EndHorizontal();
        }

        GUILayout.EndVertical();

        if (GUI.changed)
            canvas.OnNodeChange(this);
    }

    protected abstract void DrawOptionKey(int i);
    protected abstract void DrawOptionValue(int i);

    protected void DrawDoubleOptionKey(List<double> keys, int i)
    {
        keys[i] = RTEditorGUI.FloatField(GUIContent.none, (float) keys[i], BoxLayout);
    }

    protected void DrawDoubleOptionValue(List<double> values, int i, bool preview = false)
    {
        if (OptionKnobs[i].connected())
        {
            if (preview)
            {
                GUI.enabled = false;
                RTEditorGUI.FloatField(GUIContent.none, (float) Math.Round(values[i], 2), BoxLayout);
                GUI.enabled = true;
            }
            else
            {
                GUILayout.Label("Option " + (i + 1), BoxLayout);
            }
        }
        else
        {
            values[i] = RTEditorGUI.FloatField(GUIContent.none, (float) values[i], BoxLayout);
        }
    }

    protected void RefreshPreview<T>(Func<T, TVal> conversion)
    {
        List<ISupplier<T>> suppliers = [];

        for (int i = 0; i < Math.Min(Values.Count, OptionKnobs.Count); i++)
        {
            var knob = OptionKnobs[i];
            var supplier = GetIfConnected<T>(knob);
            supplier?.ResetState();
            suppliers.Add(supplier);
        }

        for (var i = 0; i < suppliers.Count; i++)
        {
            if (suppliers[i] != null) Values[i] = conversion(suppliers[i].Get());
        }
    }

    public override void CleanUpGUI()
    {
        for (var i = 0; i < OptionKnobs.Count; i++)
        {
            if (OptionKnobs[i].connected()) Values[i] = default;
        }
    }

    public override void FillNodeActionsMenu(NodeEditorInputInfo inputInfo, GenericMenu menu)
    {
        base.FillNodeActionsMenu(inputInfo, menu);

        if (!Interpolated && SupportsInterpolation)
        {
            menu.AddSeparator("");

            menu.AddItem(new GUIContent("Add interpolation"), false, () =>
            {
                Interpolated = true;
                Thresholds.Insert(0, default);
                canvas.OnNodeChange(this);
            });
        }

        if (Interpolated)
        {
            menu.AddSeparator("");

            menu.AddItem(new GUIContent("Remove interpolation"), false, () =>
            {
                Interpolated = false;
                Thresholds.RemoveAt(0);
                canvas.OnNodeChange(this);
            });
        }

        menu.AddSeparator("");

        if (OptionKnobs.Count < 20)
        {
            menu.AddItem(new GUIContent("Add branch"), false, () =>
            {
                CreateNewOptionKnob();
                RefreshDynamicKnobs();
                canvas.OnNodeChange(this);
            });
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
            List<double> thresholds,
            Interpolation<T> interpolation)
        {
            _input = input;
            _options = options;
            _thresholds = thresholds;
            _interpolation = interpolation;
        }

        public T Get()
        {
            var value = _input.Get();

            if (_interpolation != null)
            {
                for (int i = 0; i < _options.Count; i++)
                {
                    if (value < _thresholds[i])
                    {
                        if (i == 0) return _options[i].Get();
                        var t = (value - _thresholds[i - 1]) / (_thresholds[i] - _thresholds[i - 1]);
                        return _interpolation(t, _options[i - 1].Get(), _options[i].Get());
                    }
                }
            }
            else
            {
                for (int i = 0; i < _options.Count - 1; i++)
                {
                    if (value < _thresholds[i]) return _options[i].Get();
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

    protected class CurveOutput<T> : ISupplier<ICurveFunction<T>>
    {
        private readonly ISupplier<ICurveFunction<double>> _input;
        private readonly List<ISupplier<ICurveFunction<T>>> _options;
        private readonly List<double> _thresholds;
        private readonly Interpolation<T> _interpolation;

        public CurveOutput(
            ISupplier<ICurveFunction<double>> input, List<ISupplier<ICurveFunction<T>>> options,
            List<double> thresholds, Interpolation<T> interpolation)
        {
            _input = input;
            _options = options;
            _thresholds = thresholds;
            _interpolation = interpolation;
        }

        public ICurveFunction<T> Get()
        {
            if (_interpolation != null)
            {
                return new CurveFunction.Interpolate<T>(
                    _input.Get(), _options.Select(o => o.Get()).ToList(), _thresholds, _interpolation
                );
            }

            return new CurveFunction.Select<T>(
                _input.Get(), _options.Select(o => o.Get()).ToList(), _thresholds
            );
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

        public GridOutput(
            ISupplier<IGridFunction<double>> input, List<ISupplier<IGridFunction<T>>> options,
            List<double> thresholds, Interpolation<T> interpolation)
        {
            _input = input;
            _options = options;
            _thresholds = thresholds;
            _interpolation = interpolation;
        }

        public IGridFunction<T> Get()
        {
            if (_interpolation != null)
            {
                return new GridFunction.Interpolate<T>(
                    _input.Get(), _options.Select(o => o.Get()).ToList(), _thresholds, _interpolation
                );
            }

            return new GridFunction.Select<T>(
                _input.Get(), _options.Select(o => o.Get()).ToList(), _thresholds
            );
        }

        public void ResetState()
        {
            _input.ResetState();
            foreach (var option in _options) option.ResetState();
        }
    }
}
