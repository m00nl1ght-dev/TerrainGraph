using System;
using NodeEditorFramework;
using UnityEngine;

namespace TerrainGraph;

[Serializable]
[Node(false, "Path/Follow", 606)]
public class NodePathFollow : NodeBase
{
    public const string ID = "pathFollow";
    public override string GetID => ID;

    public override string Title => "Path: Follow";

    [ValueConnectionKnob("Input", Direction.In, PathFunctionConnection.Id)]
    public ValueConnectionKnob InputKnob;

    [ValueConnectionKnob("Absolute Grid", Direction.In, GridFunctionConnection.Id)]
    public ValueConnectionKnob AbsGridKnob;

    [ValueConnectionKnob("Relative Grid", Direction.In, GridFunctionConnection.Id)]
    public ValueConnectionKnob RelGridKnob;

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
        GUILayout.Label("Absolute Grid", BoxLayout);
        GUILayout.EndHorizontal();

        AbsGridKnob.SetPosition();

        GUILayout.BeginHorizontal(BoxStyle);
        GUILayout.Label("Relative Grid", BoxLayout);
        GUILayout.EndHorizontal();

        RelGridKnob.SetPosition();

        GUILayout.EndVertical();

        if (GUI.changed)
            canvas.OnNodeChange(this);
    }

    public override bool Calculate()
    {
        OutputKnob.SetValue<ISupplier<Path>>(new Output(
            SupplierOrFixed(InputKnob, Path.Empty),
            SupplierOrGridFixed(AbsGridKnob, GridFunction.Zero),
            SupplierOrGridFixed(RelGridKnob, GridFunction.Zero)
        ));
        return true;
    }

    private class Output : ISupplier<Path>
    {
        private readonly ISupplier<Path> _input;
        private readonly ISupplier<IGridFunction<double>> _absGrid;
        private readonly ISupplier<IGridFunction<double>> _relGrid;

        public Output(
            ISupplier<Path> input,
            ISupplier<IGridFunction<double>> absGrid,
            ISupplier<IGridFunction<double>> relGrid)
        {
            _input = input;
            _absGrid = absGrid;
            _relGrid = relGrid;
        }

        public Path Get()
        {
            var path = new Path(_input.Get());

            foreach (var segment in path.Leaves())
            {
                var extParams = segment.ExtendParams;

                extParams.AbsFollowGrid = _absGrid.Get();
                extParams.RelFollowGrid = _relGrid.Get();

                segment.ExtendWithParams(extParams);
            }

            return path;
        }

        public void ResetState()
        {
            _input.ResetState();
            _absGrid.ResetState();
            _relGrid.ResetState();
        }
    }
}
