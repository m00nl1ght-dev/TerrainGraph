using System;
using System.Collections.Generic;
using NodeEditorFramework;
using TerrainGraph.Util;

namespace TerrainGraph;

[Serializable]
[Node(false, "Value/Operator", 110)]
public class NodeValueOperator : NodeOperatorBase
{
    public const string ID = "valueOperator";
    public override string GetID => ID;

    [ValueConnectionKnob("Output", Direction.Out, ValueFunctionConnection.Id)]
    public ValueConnectionKnob OutputKnob;

    public override ValueConnectionKnob OutputKnobRef => OutputKnob;

    protected override void InputGUI(int i)
    {
        var value = Values[i];
        KnobValueField(InputKnobs[i], ref value, i == 0 ? "Base" : "Input " + i);
        Values[i] = value;
    }

    protected override void CreateNewInputKnob()
    {
        CreateValueConnectionKnob(new("Input " + InputKnobs.Count, Direction.In, ValueFunctionConnection.Id));
        RefreshDynamicKnobs();
        canvas.OnNodeChange(this);
    }

    public override void RefreshPreview()
    {
        base.RefreshPreview();
        List<ISupplier<double>> suppliers = [];

        for (int i = 0; i < Math.Min(Values.Count, InputKnobs.Count); i++)
        {
            var knob = InputKnobs[i];
            var supplier = GetIfConnected<double>(knob);
            supplier?.ResetState();
            suppliers.Add(supplier);
        }

        for (var i = 0; i < suppliers.Count; i++)
        {
            if (suppliers[i] != null) Values[i] = suppliers[i].Get();
        }
    }

    public override bool Calculate()
    {
        var applyChance = SupplierOrFallback(ApplyChanceKnob, ApplyChance);
        var smoothness = SupplierOrFallback(SmoothnessKnob, Smoothness);
        var stackCount = SupplierOrFallback(StackCountKnob, StackCount);

        List<ISupplier<double>> inputs = [];
        for (int i = 0; i < Math.Min(Values.Count, InputKnobs.Count); i++)
        {
            inputs.Add(SupplierOrFallback(InputKnobs[i], Values[i]));
        }

        OutputKnob.SetValue<ISupplier<double>>(new Output(
            applyChance, stackCount, inputs, OperationType,
            smoothness, CombinedSeed, TerrainCanvas.CreateRandomInstance()
        ));
        return true;
    }

    public static Func<double, double, double> BuildFunc(Operation operation, double smoothness)
    {
        Func<double, double, double> func = operation switch
        {
            Operation.Add => (a, b) => a + b,
            Operation.Subtract => (a, b) => a - b,
            Operation.Multiply => (a, b) => a * b,
            Operation.Divide => (a, b) => a / b,
            Operation.Min or Operation.Smooth_Min => (a, b) => GridFunction.Min.Calculate(a, b, smoothness),
            Operation.Max or Operation.Smooth_Max => (a, b) => GridFunction.Max.Calculate(a, b, smoothness),
            Operation.Invert => (a, b) => b + (b - a),
            Operation.Invert_Below => (a, b) => a < b ? b + (b - a) : a,
            Operation.Invert_Above => (a, b) => a > b ? b + (b - a) : a,
            Operation.Scale_Around_1 => (a, b) => a.ScaleAround(1, b),
            _ => throw new ArgumentOutOfRangeException()
        };
        return func;
    }

    private class Output : ISupplier<double>
    {
        private readonly ISupplier<double> _applyChance;
        private readonly ISupplier<double> _stackCount;
        private readonly List<ISupplier<double>> _inputs;
        private readonly Operation _operationType;
        private readonly ISupplier<double> _smoothness;
        private readonly int _seed;
        private readonly IRandom _random;

        public Output(
            ISupplier<double> applyChance,
            ISupplier<double> stackCount,
            List<ISupplier<double>> inputs,
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

        public double Get()
        {
            double applyChance = _applyChance.Get();
            double stackCount = _stackCount.Get();
            double smoothness = _smoothness.Get();

            if (stackCount < 1) stackCount = 1;
            else if (stackCount > 20) stackCount = 20;

            if (_inputs.Count == 0) return 0f;

            var func = BuildFunc(_operationType, smoothness);

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
