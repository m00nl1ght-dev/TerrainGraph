using System;
using System.Collections.Generic;
using NodeEditorFramework;
using NodeEditorFramework.Utilities;
using TerrainGraph.Util;
using UnityEngine;

namespace TerrainGraph;

[Serializable]
[Node(false, "Grid/Operator", 211)]
public class NodeGridOperator : NodeOperatorBase
{
    public const string ID = "gridOperator";
    public override string GetID => ID;

    [ValueConnectionKnob("Output", Direction.Out, GridFunctionConnection.Id)]
    public ValueConnectionKnob OutputKnob;

    public override ValueConnectionKnob OutputKnobRef => OutputKnob;

    public List<double> Values = new();

    public override void NodeGUI()
    {
        OutputKnob.SetPosition(FirstKnobPosition);
        while (InputKnobs.Count < 2) CreateNewInputKnob();

        GUILayout.BeginVertical(BoxStyle);

        if (SmoothnessKnob != null) KnobValueField(SmoothnessKnob, ref Smoothness);
        if (ApplyChanceKnob != null) KnobValueField(ApplyChanceKnob, ref ApplyChance);
        if (StackCountKnob != null) KnobValueField(StackCountKnob, ref StackCount);

        while (Values.Count < InputKnobs.Count) Values.Add(0f);
        while (Values.Count > InputKnobs.Count) Values.RemoveAt(Values.Count - 1);

        for (int i = 0; i < InputKnobs.Count; i++)
        {
            var knob = InputKnobs[i];

            GUILayout.BeginHorizontal(BoxStyle);
            GUILayout.Label(i == 0 ? "Base" : ("Input " + i), BoxLayout);

            if (!knob.connected())
            {
                GUILayout.FlexibleSpace();
                Values[i] = RTEditorGUI.FloatField(GUIContent.none, (float) Values[i], BoxLayout);
            }

            GUILayout.EndHorizontal();
            knob.SetPosition();
        }

        GUILayout.EndVertical();

        if (GUI.changed)
            canvas.OnNodeChange(this);
    }

    protected override void CreateNewInputKnob()
    {
        CreateValueConnectionKnob(new("Input " + InputKnobs.Count, Direction.In, GridFunctionConnection.Id));
        RefreshDynamicKnobs();
    }

    public override bool Calculate()
    {
        var applyChance = SupplierOrValueFixed(ApplyChanceKnob, ApplyChance);
        var smoothness = SupplierOrValueFixed(SmoothnessKnob, Smoothness);
        var stackCount = SupplierOrValueFixed(StackCountKnob, StackCount);

        List<ISupplier<IGridFunction<double>>> inputs = new();
        for (int i = 0; i < Math.Min(Values.Count, InputKnobs.Count); i++)
        {
            inputs.Add(SupplierOrGridFixed(InputKnobs[i], GridFunction.Of(Values[i])));
        }

        OutputKnob.SetValue<ISupplier<IGridFunction<double>>>(new Output(
            applyChance, stackCount, inputs, OperationType, smoothness,
            CombinedSeed, TerrainCanvas.CreateRandomInstance()
        ));
        return true;
    }

    private class Output : ISupplier<IGridFunction<double>>
    {
        private readonly ISupplier<double> _applyChance;
        private readonly ISupplier<double> _stackCount;
        private readonly List<ISupplier<IGridFunction<double>>> _inputs;
        private readonly Operation _operationType;
        private readonly ISupplier<double> _smoothness;
        private readonly int _seed;
        private readonly IRandom _random;

        public Output(
            ISupplier<double> applyChance,
            ISupplier<double> stackCount,
            List<ISupplier<IGridFunction<double>>> inputs,
            Operation operationType,
            ISupplier<double> smoothness,
            int seed, IRandom random)
        {
            _applyChance = applyChance;
            _stackCount = stackCount;
            _inputs = inputs;
            _operationType = operationType;
            _smoothness = smoothness;
            _seed = seed;
            _random = random;
            _random.Reinitialise(_seed);
        }

        public IGridFunction<double> Get()
        {
            double applyChance = _applyChance.Get();
            double stackCount = _stackCount.Get();
            double smoothness = _smoothness.Get();

            if (stackCount < 1) stackCount = 1;
            else if (stackCount > 20) stackCount = 20;

            if (_inputs.Count == 0) return GridFunction.Zero;

            Func<IGridFunction<double>, IGridFunction<double>, IGridFunction<double>> func = _operationType switch
            {
                Operation.Add => (a, b) => new GridFunction.Add(a, b),
                Operation.Multiply => (a, b) => new GridFunction.Multiply(a, b),
                Operation.Min or Operation.Smooth_Min => (a, b) => new GridFunction.Min(a, b, smoothness),
                Operation.Max or Operation.Smooth_Max => (a, b) => new GridFunction.Max(a, b, smoothness),
                _ => throw new ArgumentOutOfRangeException()
            };

            var value = _inputs[0].Get();

            for (int s = 0; s < stackCount; s++)
            {
                for (int i = 1; i < _inputs.Count; i++)
                {
                    if (applyChance >= 1 || _random.NextDouble() < applyChance)
                    {
                        value = func(value, _inputs[i].Get());
                    }
                }
            }

            return value;
        }

        public void ResetState()
        {
            _random.Reinitialise(_seed);
            foreach (var input in _inputs) input.ResetState();
            _applyChance.ResetState();
            _stackCount.ResetState();
            _smoothness.ResetState();
        }
    }
}
