using System;
using System.Linq;
using NodeEditorFramework;
using TerrainGraph.Flow;
using UnityEngine;

namespace TerrainGraph;

[Serializable]
[Node(false, "Path/End Condition", 622)]
public class NodePathEndCondition : NodeBase
{
    public const string ID = "pathEndCondition";
    public override string GetID => ID;

    public override string Title => "Path: End Condition";

    [ValueConnectionKnob("Input", Direction.In, PathFunctionConnection.Id)]
    public ValueConnectionKnob InputKnob;

    [ValueConnectionKnob("Width mask", Direction.In, GridFunctionConnection.Id)]
    public ValueConnectionKnob WidthMaskKnob;

    [ValueConnectionKnob("Output", Direction.Out, PathFunctionConnection.Id)]
    public ValueConnectionKnob OutputKnob;

    public override void NodeGUI()
    {
        InputKnob.SetPosition(FirstKnobPosition);
        OutputKnob.SetPosition(FirstKnobPosition);

        GUILayout.BeginVertical(BoxStyle);

        GUILayout.BeginHorizontal(BoxStyle);
        GUILayout.Label("Input", BoxLayout);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal(BoxStyle);
        GUILayout.Label("Width mask", BoxLayout);
        GUILayout.EndHorizontal();

        WidthMaskKnob.SetPosition();

        GUILayout.EndVertical();

        if (GUI.changed)
            canvas.OnNodeChange(this);
    }

    public override bool Calculate()
    {
        OutputKnob.SetValue<ISupplier<Path>>(new Output(
            SupplierOrFallback(InputKnob, Path.Empty),
            SupplierOrFallback(WidthMaskKnob, GridFunction.Zero),
            TerrainCanvas.GridFullSize / (double) TerrainCanvas.GridPathSize
        ));
        return true;
    }

    private class Output : ISupplier<Path>
    {
        private readonly ISupplier<Path> _input;
        private readonly ISupplier<IGridFunction<double>> _widthMask;
        private readonly double _gridScale;

        public Output(ISupplier<Path> input, ISupplier<IGridFunction<double>> widthMask, double gridScale)
        {
            _input = input;
            _widthMask = widthMask;
            _gridScale = gridScale;
        }

        public Path Get()
        {
            var path = new Path(_input.Get());

            foreach (var segment in path.Leaves.ToList())
            {
                var extParams = segment.TraceParams;

                extParams.EndCondition = _widthMask.Get().Scaled(_gridScale);

                segment.ExtendWithParams(extParams);
            }

            return path;
        }

        public void ResetState()
        {
            _input.ResetState();
            _widthMask.ResetState();
        }
    }
}
