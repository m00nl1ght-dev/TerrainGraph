using System;
using NodeEditorFramework;
using NodeEditorFramework.Utilities;
using UnityEngine;

namespace TerrainGraph;

[Serializable]
[Node(false, "Grid/Validate", 213)]
public class NodeGridValidate : NodeBase
{
    public const string ID = "gridValidate";
    public override string GetID => ID;

    public override string Title => EdgeCellsOnly ? "Validate Edges" : "Validate";

    [ValueConnectionKnob("Input", Direction.In, GridFunctionConnection.Id)]
    public ValueConnectionKnob InputKnob;

    [ValueConnectionKnob("Min value", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob MinValueKnob;

    [ValueConnectionKnob("Max value", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob MaxValueKnob;

    [ValueConnectionKnob("Min cells", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob MinCellsKnob;

    [ValueConnectionKnob("Max cells", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob MaxCellsKnob;

    [ValueConnectionKnob("Output", Direction.Out, GridFunctionConnection.Id)]
    public ValueConnectionKnob OutputKnob;

    public double MinValue;
    public double MaxValue;

    public double MinCells;
    public double MaxCells;

    public int MaxTries = 3;

    public bool EdgeCellsOnly;

    public override void NodeGUI()
    {
        InputKnob.SetPosition(FirstKnobPosition);
        OutputKnob.SetPosition(FirstKnobPosition);

        GUILayout.BeginVertical(BoxStyle);

        GUILayout.BeginHorizontal(BoxStyle);
        GUILayout.Label("Input", BoxLayout);
        GUILayout.EndHorizontal();

        KnobValueField(MinValueKnob, ref MinValue);
        KnobValueField(MaxValueKnob, ref MaxValue);
        KnobValueField(MinCellsKnob, ref MinCells);
        KnobValueField(MaxCellsKnob, ref MaxCells);

        IntField("Max tries", ref MaxTries);

        GUILayout.EndVertical();

        if (GUI.changed)
            canvas.OnNodeChange(this);
    }

    public override void RefreshPreview()
    {
        var minValue = GetIfConnected<double>(MinValueKnob);
        var maxValue = GetIfConnected<double>(MaxValueKnob);
        var minCells = GetIfConnected<double>(MinCellsKnob);
        var maxCells = GetIfConnected<double>(MaxCellsKnob);

        minValue?.ResetState();
        maxValue?.ResetState();
        minCells?.ResetState();
        maxCells?.ResetState();

        if (minValue != null) MinValue = minValue.Get();
        if (maxValue != null) MaxValue = maxValue.Get();
        if (minCells != null) MinCells = minCells.Get();
        if (maxCells != null) MaxCells = maxCells.Get();
    }

    public override void FillNodeActionsMenu(NodeEditorInputInfo inputInfo, GenericMenu menu)
    {
        base.FillNodeActionsMenu(inputInfo, menu);
        menu.AddSeparator("");

        if (EdgeCellsOnly)
        {
            menu.AddItem(new GUIContent("Switch to full"), false, () =>
            {
                EdgeCellsOnly = false;
                canvas.OnNodeChange(this);
            });
        }
        else
        {
            menu.AddItem(new GUIContent("Switch to edges"), false, () =>
            {
                EdgeCellsOnly = true;
                canvas.OnNodeChange(this);
            });
        }
    }

    public override bool Calculate()
    {
        OutputKnob.SetValue<ISupplier<IGridFunction<double>>>(new Output(
            SupplierOrGridFixed(InputKnob, GridFunction.Zero),
            SupplierOrValueFixed(MinValueKnob, MinValue),
            SupplierOrValueFixed(MaxValueKnob, MaxValue),
            SupplierOrValueFixed(MinCellsKnob, MinCells),
            SupplierOrValueFixed(MaxCellsKnob, MaxCells),
            MaxTries, GridSize, EdgeCellsOnly
        ));
        return true;
    }

    public static int Validate(IGridFunction<double> input, int sizeX, int sizeZ, double minValue, double maxValue, bool edgeCellsOnly)
    {
        var accepted = 0;

        for (int x = 0; x < sizeX; x++)
        {
            if (edgeCellsOnly && x != 0 && x != sizeX - 1)
            {
                var first = input.ValueAt(x, 0);
                if (first >= minValue && first <= maxValue) accepted++;

                var last = input.ValueAt(x, sizeZ - 1);
                if (last >= minValue && last <= maxValue) accepted++;
            }
            else
            {
                for (int z = 0; z < sizeZ; z++)
                {
                    var value = input.ValueAt(x, z);
                    if (value >= minValue && value <= maxValue) accepted++;
                }
            }
        }

        return accepted;
    }

    public static int ValidateRow(IGridFunction<double> input, int z, int sizeX, double minValue, double maxValue)
    {
        var accepted = 0;

        for (int x = 0; x < sizeX; x++)
        {
            var value = input.ValueAt(x, z);
            if (value >= minValue && value <= maxValue) accepted++;
        }

        return accepted;
    }

    public static int ValidateCol(IGridFunction<double> input, int x, int sizeZ, double minValue, double maxValue)
    {
        var accepted = 0;

        for (int z = 0; z < sizeZ; z++)
        {
            var value = input.ValueAt(x, z);
            if (value >= minValue && value <= maxValue) accepted++;
        }

        return accepted;
    }

    public class Output : ISupplier<IGridFunction<double>>
    {
        private readonly ISupplier<IGridFunction<double>> _input;

        private readonly ISupplier<double> _minValue;
        private readonly ISupplier<double> _maxValue;
        private readonly ISupplier<double> _minCells;
        private readonly ISupplier<double> _maxCells;

        private readonly int _maxTries;
        private readonly double _gridSize;
        private readonly bool _edgeCellsOnly;

        public Output(
            ISupplier<IGridFunction<double>> input,
            ISupplier<double> minValue, ISupplier<double> maxValue,
            ISupplier<double> minCells, ISupplier<double> maxCells,
            int maxTries, double gridSize, bool edgeCellsOnly)
        {
            _input = input;
            _minValue = minValue;
            _maxValue = maxValue;
            _minCells = minCells;
            _maxCells = maxCells;
            _maxTries = maxTries;
            _gridSize = gridSize;
            _edgeCellsOnly = edgeCellsOnly;
        }

        public IGridFunction<double> Get()
        {
            var minValue = _minValue.Get();
            var maxValue = _maxValue.Get();
            var minCells = _minCells.Get();
            var maxCells = _maxCells.Get();

            int maxTries = _maxTries <= 10 ? _maxTries : 10;
            int gridSize = (int) _gridSize;

            IGridFunction<double> input = null;

            var acceptedMin = int.MaxValue;
            var acceptedMax = int.MinValue;

            if (maxTries < 1) maxTries = 1;

            for (int i = 0; i < maxTries; i++)
            {
                input = _input.Get();

                var accepted = Validate(input, gridSize, gridSize, minValue, maxValue, _edgeCellsOnly);

                if (accepted >= minCells && accepted <= maxCells) return input;

                if (accepted > acceptedMax) acceptedMax = accepted;
                if (accepted < acceptedMin) acceptedMin = accepted;
            }

            Debug.Log($"Grid function failed validation after {maxTries} tries, reaching {acceptedMin}-{acceptedMax} accepted cells, " +
                      $"but needing {minCells:F0}-{maxCells:F0} to be in value range {minValue:F2}-{maxValue:F2}.");

            return input;
        }

        public void ResetState()
        {
            _input.ResetState();
            _minValue.ResetState();
            _maxValue.ResetState();
            _minCells.ResetState();
            _maxCells.ResetState();
        }
    }
}
