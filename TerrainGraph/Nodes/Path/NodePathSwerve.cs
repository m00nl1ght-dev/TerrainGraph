using System;
using NodeEditorFramework;
using UnityEngine;

namespace TerrainGraph;

[Serializable]
[Node(false, "Path/Swerve", 605)]
public class NodePathSwerve : NodeBase
{
    public const string ID = "pathSwerve";
    public override string GetID => ID;

    public override string Title => "Path: Swerve";

    [ValueConnectionKnob("Input", Direction.In, PathFunctionConnection.Id)]
    public ValueConnectionKnob InputKnob;

    [ValueConnectionKnob("Angle Change", Direction.In, GridFunctionConnection.Id)]
    public ValueConnectionKnob AngleGridKnob;

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
        GUILayout.Label("Angle Change", BoxLayout);
        GUILayout.EndHorizontal();

        AngleGridKnob.SetPosition();

        GUILayout.EndVertical();

        if (GUI.changed)
            canvas.OnNodeChange(this);
    }

    public override bool Calculate()
    {
        OutputKnob.SetValue<ISupplier<Path>>(new Output(
            SupplierOrFallback(InputKnob, Path.Empty),
            SupplierOrFallback(AngleGridKnob, GridFunction.Zero)
        ));
        return true;
    }

    private class Output : ISupplier<Path>
    {
        private readonly ISupplier<Path> _input;
        private readonly ISupplier<IGridFunction<double>> _angleGrid;

        public Output(ISupplier<Path> input, ISupplier<IGridFunction<double>> angleGrid)
        {
            _input = input;
            _angleGrid = angleGrid;
        }

        public Path Get()
        {
            var path = new Path(_input.Get());

            foreach (var segment in path.Leaves())
            {
                var extParams = segment.ExtendParams;

                extParams.SwerveGrid = _angleGrid.Get();

                segment.ExtendWithParams(extParams);
            }

            return path;
        }

        public void ResetState()
        {
            _input.ResetState();
            _angleGrid.ResetState();
        }
    }
}
