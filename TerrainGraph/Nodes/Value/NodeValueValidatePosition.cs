using System;
using System.Collections.Generic;
using System.Linq;
using NodeEditorFramework;
using TerrainGraph.Util;
using UnityEngine;

namespace TerrainGraph;

[Serializable]
[Node(false, "Value/Validate Position", 116)]
public class NodeValueValidatePosition : NodeBase
{
    public const string ID = "valueValidatePosition";
    public override string GetID => ID;

    public override string Title => "Validate Position";

    [ValueConnectionKnob("InputX", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob InputXKnob;

    [ValueConnectionKnob("InputZ", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob InputZKnob;

    [ValueConnectionKnob("Validator", Direction.In, GridFunctionConnection.Id)]
    public ValueConnectionKnob ValidatorKnob;

    [ValueConnectionKnob("Exclusion", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob ExclusionRadiusKnob;

    [ValueConnectionKnob("OutputX", Direction.Out, ValueFunctionConnection.Id)]
    public ValueConnectionKnob OutputXKnob;

    [ValueConnectionKnob("OutputZ", Direction.Out, ValueFunctionConnection.Id)]
    public ValueConnectionKnob OutputZKnob;

    public double ExclusionRadius;

    public int Attempts = 100;

    public override void NodeGUI()
    {
        GUILayout.BeginVertical(BoxStyle);

        GUILayout.BeginHorizontal(BoxStyle);
        GUILayout.Label("Position x", BoxLayout);
        GUILayout.EndHorizontal();

        InputXKnob.SetPosition();
        OutputXKnob.SetPosition();

        GUILayout.BeginHorizontal(BoxStyle);
        GUILayout.Label("Position z", BoxLayout);
        GUILayout.EndHorizontal();

        InputZKnob.SetPosition();
        OutputZKnob.SetPosition();

        GUILayout.BeginHorizontal(BoxStyle);
        GUILayout.Label("Validator", BoxLayout);
        GUILayout.EndHorizontal();

        ValidatorKnob.SetPosition();

        KnobValueField(ExclusionRadiusKnob, ref ExclusionRadius);

        IntField("Attempts", ref Attempts);

        GUILayout.EndVertical();

        if (GUI.changed)
            canvas.OnNodeChange(this);
    }

    public override void RefreshPreview()
    {
        var exclusionRadius = GetIfConnected<double>(ExclusionRadiusKnob);

        exclusionRadius?.ResetState();

        if (exclusionRadius != null) ExclusionRadius = exclusionRadius.Get();
    }

    public override bool Calculate()
    {
        var cache = new List<Vector2d>(5);

        var output = new Output(
            SupplierOrFallback(InputXKnob, 0d),
            SupplierOrFallback(InputZKnob, 0d),
            SupplierOrFallback(ValidatorKnob, GridFunction.One),
            SupplierOrFallback(ExclusionRadiusKnob, ExclusionRadius),
            cache, TerrainCanvas.GridFullSize, Attempts.InRange(1, 1000)
        );

        OutputXKnob.SetValue<ISupplier<double>>(
            new Supplier.CompoundCached<Vector2d,double>(output, t => t.x, cache)
        );

        OutputZKnob.SetValue<ISupplier<double>>(
            new Supplier.CompoundCached<Vector2d,double>(output, t => t.z, cache)
        );

        return true;
    }

    private class Output : ISupplier<Vector2d>
    {
        private readonly ISupplier<double> _inputX;
        private readonly ISupplier<double> _inputZ;
        private readonly ISupplier<IGridFunction<double>> _validator;
        private readonly ISupplier<double> _exclusionRadius;
        private readonly IReadOnlyList<Vector2d> _usedPositions;
        private readonly int _gridSize;
        private readonly int _attempts;

        public Output(
            ISupplier<double> inputX,
            ISupplier<double> inputZ,
            ISupplier<IGridFunction<double>> validator,
            ISupplier<double> exclusionRadius,
            IReadOnlyList<Vector2d> usedPositions,
            int gridSize, int attempts)
        {
            _inputX = inputX;
            _inputZ = inputZ;
            _validator = validator;
            _exclusionRadius = exclusionRadius;
            _usedPositions = usedPositions;
            _gridSize = gridSize;
            _attempts = attempts;
        }

        public Vector2d Get()
        {
            var validator = _validator.Get();
            var exclusionRadius = _exclusionRadius.Get();

            var attempt = 0;

            var pos = Vector2d.Zero;

            while (attempt++ < _attempts)
            {
                pos = new Vector2d(_inputX.Get(), _inputZ.Get());

                if (validator.ValueAt(pos * _gridSize) > 0)
                {
                    if (exclusionRadius <= 0) break;
                    if (!_usedPositions.Any(p => Vector2d.Distance(pos, p) <= exclusionRadius)) break;
                }
            }

            #if DEBUG
            Debug.Log($"NodeValueValidatePosition used {attempt} of {_attempts} attempts");
            #endif

            return pos;
        }

        public void ResetState()
        {
            _inputX.ResetState();
            _inputZ.ResetState();
            _validator.ResetState();
            _exclusionRadius.ResetState();
        }
    }
}
