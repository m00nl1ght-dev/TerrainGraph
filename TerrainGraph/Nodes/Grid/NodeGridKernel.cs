using System;
using NodeEditorFramework;
using NodeEditorFramework.Utilities;
using TerrainGraph.Util;
using UnityEngine;
using static TerrainGraph.NodeOperatorBase;

namespace TerrainGraph;

[Serializable]
[Node(false, "Grid/Kernel", 214)]
public class NodeGridKernel : NodeBase
{
    public const string ID = "gridKernel";
    public override string GetID => ID;

    public override string Title => $"Kernel ({OperationType.ToString().Replace('_', ' ')})";

    [ValueConnectionKnob("Input", Direction.In, GridFunctionConnection.Id)]
    public ValueConnectionKnob InputKnob;

    [ValueConnectionKnob("Size", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob SizeKnob;

    [ValueConnectionKnob("Step", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob StepKnob;

    [ValueConnectionKnob("Output", Direction.Out, GridFunctionConnection.Id)]
    public ValueConnectionKnob OutputKnob;

    public Operation OperationType = Operation.Add;

    public double Size = 1;
    public double Step = 1;

    public override void NodeGUI()
    {
        InputKnob.SetPosition(FirstKnobPosition);
        OutputKnob.SetPosition(FirstKnobPosition);

        GUILayout.BeginVertical(BoxStyle);

        GUILayout.BeginHorizontal(BoxStyle);
        GUILayout.Label("Input", BoxLayout);
        GUILayout.EndHorizontal();

        KnobValueField(SizeKnob, ref Size);
        KnobValueField(StepKnob, ref Step);

        GUILayout.EndVertical();

        if (GUI.changed)
            canvas.OnNodeChange(this);
    }

    public override void RefreshPreview()
    {
        var sizeSupplier = GetIfConnected<double>(SizeKnob);
        var stepSupplier = GetIfConnected<double>(StepKnob);

        sizeSupplier?.ResetState();
        stepSupplier?.ResetState();

        if (sizeSupplier != null) Size = sizeSupplier.Get();
        if (stepSupplier != null) Step = stepSupplier.Get();
    }

    public override void FillNodeActionsMenu(NodeEditorInputInfo inputInfo, GenericMenu menu)
    {
        base.FillNodeActionsMenu(inputInfo, menu);

        menu.AddSeparator("");

        SelectionMenu<Operation>(menu, op => OperationType = op, "Change operation/");
    }

    public override bool Calculate()
    {
        OutputKnob.SetValue<ISupplier<IGridFunction<double>>>(new Output(
            SupplierOrFallback(InputKnob, GridFunction.Zero),
            SupplierOrFallback(SizeKnob, Size),
            SupplierOrFallback(StepKnob, Step),
            OperationType
        ));
        return true;
    }

    public class Output : ISupplier<IGridFunction<double>>
    {
        private readonly ISupplier<IGridFunction<double>> _input;
        private readonly ISupplier<double> _size;
        private readonly ISupplier<double> _step;
        private readonly Operation _operation;

        public Output(
            ISupplier<IGridFunction<double>> input,
            ISupplier<double> size,
            ISupplier<double> step,
            Operation operation)
        {
            _input = input;
            _size = size;
            _step = step;
            _operation = operation;
        }

        public IGridFunction<double> Get()
        {
            var func = NodeValueOperator.BuildFunc(_operation, 0d);
            var kernel = GridKernel.Square((int) _size.Get(), _step.Get());
            return new GridFunction.KernelAggregation<double>(_input.Get(), func, kernel);
        }

        public void ResetState()
        {
            _input.ResetState();
            _size.ResetState();
            _step.ResetState();
        }
    }
}
