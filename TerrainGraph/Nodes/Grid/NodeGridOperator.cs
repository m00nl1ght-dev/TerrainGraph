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

    protected override void CreateNewInputKnob()
    {
        CreateValueConnectionKnob(new("Input " + InputKnobs.Count, Direction.In, GridFunctionConnection.Id));
        RefreshDynamicKnobs();
    }

    public override bool Calculate()
    {
        var applyChance = SupplierOrFallback(ApplyChanceKnob, ApplyChance);
        var smoothness = SupplierOrFallback(SmoothnessKnob, Smoothness);
        var stackCount = SupplierOrFallback(StackCountKnob, StackCount);

        List<ISupplier<IGridFunction<double>>> inputs = [];
        for (int i = 0; i < Math.Min(Values.Count, InputKnobs.Count); i++)
        {
            inputs.Add(SupplierOrFallback(InputKnobs[i], GridFunction.Of(Values[i])));
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
                Operation.Subtract => (a, b) => new GridFunction.Subtract(a, b),
                Operation.Multiply => (a, b) => new GridFunction.Multiply(a, b),
                Operation.Divide => (a, b) => new GridFunction.Divide(a, b),
                Operation.Min or Operation.Smooth_Min => (a, b) => new GridFunction.Min(a, b, smoothness),
                Operation.Max or Operation.Smooth_Max => (a, b) => new GridFunction.Max(a, b, smoothness),
                Operation.Invert => (a, b) => new GridFunction.Invert(a, b, true, true),
                Operation.Invert_Below => (a, b) => new GridFunction.Invert(a, b, true, false),
                Operation.Invert_Above => (a, b) => new GridFunction.Invert(a, b, false, true),
                Operation.Scale_Around_1 => (a, b) => new GridFunction.ScaleAround(a, GridFunction.Of(1d), b),
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
