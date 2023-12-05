using System;
using NodeEditorFramework;
using UnityEngine;

namespace TerrainGraph;

[Serializable]
[Node(false, "Grid/Transform", 212)]
public class NodeGridTransform : NodeBase
{
    public const string ID = "gridTransform";
    public override string GetID => ID;

    public override string Title => "Transform";

    [ValueConnectionKnob("Input", Direction.In, GridFunctionConnection.Id)]
    public ValueConnectionKnob InputKnob;

    [ValueConnectionKnob("DisplaceX", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob DisplaceXKnob;

    [ValueConnectionKnob("DisplaceZ", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob DisplaceZKnob;

    [ValueConnectionKnob("ScaleX", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob ScaleXKnob;

    [ValueConnectionKnob("ScaleZ", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob ScaleZKnob;

    [ValueConnectionKnob("Output", Direction.Out, GridFunctionConnection.Id)]
    public ValueConnectionKnob OutputKnob;

    public double DisplaceX;
    public double DisplaceZ;
    public double ScaleX = 1;
    public double ScaleZ = 1;

    public override void NodeGUI()
    {
        InputKnob.SetPosition(FirstKnobPosition);
        OutputKnob.SetPosition(FirstKnobPosition);

        GUILayout.BeginVertical(BoxStyle);

        GUILayout.BeginHorizontal(BoxStyle);
        GUILayout.Label("Input", BoxLayout);
        GUILayout.EndHorizontal();

        KnobValueField(DisplaceXKnob, ref DisplaceX);
        KnobValueField(DisplaceZKnob, ref DisplaceZ);
        KnobValueField(ScaleXKnob, ref ScaleX);
        KnobValueField(ScaleZKnob, ref ScaleZ);

        GUILayout.EndVertical();

        if (GUI.changed)
            canvas.OnNodeChange(this);
    }

    public override void RefreshPreview()
    {
        var displaceX = GetIfConnected<double>(DisplaceXKnob);
        var displaceZ = GetIfConnected<double>(DisplaceZKnob);
        var scaleX = GetIfConnected<double>(ScaleXKnob);
        var scaleZ = GetIfConnected<double>(ScaleZKnob);

        displaceX?.ResetState();
        displaceZ?.ResetState();
        scaleX?.ResetState();
        scaleZ?.ResetState();

        if (displaceX != null) DisplaceX = displaceX.Get();
        if (displaceZ != null) DisplaceZ = displaceZ.Get();
        if (scaleX != null) ScaleX = scaleX.Get();
        if (scaleZ != null) ScaleZ = scaleZ.Get();
    }

    public override bool Calculate()
    {
        OutputKnob.SetValue<ISupplier<IGridFunction<double>>>(new Output(
            SupplierOrFallback(InputKnob, GridFunction.Zero),
            SupplierOrFallback(DisplaceXKnob, DisplaceX),
            SupplierOrFallback(DisplaceZKnob, DisplaceZ),
            SupplierOrFallback(ScaleXKnob, ScaleX),
            SupplierOrFallback(ScaleZKnob, ScaleZ),
            GridSize
        ));
        return true;
    }

    public class Output : ISupplier<IGridFunction<double>>
    {
        private readonly ISupplier<IGridFunction<double>> _input;
        private readonly ISupplier<double> _displaceX;
        private readonly ISupplier<double> _displaceZ;
        private readonly ISupplier<double> _scaleX;
        private readonly ISupplier<double> _scaleZ;
        private readonly double _gridSize;

        public Output(
            ISupplier<IGridFunction<double>> input, ISupplier<double> displaceX, ISupplier<double> displaceZ,
            ISupplier<double> scaleX, ISupplier<double> scaleZ, double gridSize)
        {
            _input = input;
            _displaceX = displaceX;
            _displaceZ = displaceZ;
            _scaleX = scaleX;
            _scaleZ = scaleZ;
            _gridSize = gridSize;
        }

        public IGridFunction<double> Get()
        {
            return new GridFunction.Transform<double>(
                _input.Get(), _displaceX.Get() * _gridSize, _displaceZ.Get() * _gridSize,
                _scaleX.Get(), _scaleZ.Get());
        }

        public void ResetState()
        {
            _input.ResetState();
            _displaceX.ResetState();
            _displaceZ.ResetState();
            _scaleX.ResetState();
            _scaleZ.ResetState();
        }
    }
}
