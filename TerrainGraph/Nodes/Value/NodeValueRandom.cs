using System;
using NodeEditorFramework;
using NodeEditorFramework.Utilities;
using TerrainGraph.Util;
using UnityEngine;

namespace TerrainGraph;

[Serializable]
[Node(false, "Value/Random", 111)]
public class NodeValueRandom : NodeBase
{
    public const string ID = "valueRandom";
    public override string GetID => ID;

    public override string Title => (DynamicSeed ? "Dynamic " : "") + "Random Value";

    [ValueConnectionKnob("Average", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob AverageKnob;

    [ValueConnectionKnob("Deviation", Direction.In, ValueFunctionConnection.Id)]
    public ValueConnectionKnob DeviationKnob;

    [ValueConnectionKnob("Output", Direction.Out, ValueFunctionConnection.Id)]
    public ValueConnectionKnob OutputKnob;

    public double Average = 0.5;
    public double Deviation = 0.5;
    public bool DynamicSeed = true;

    public override void NodeGUI()
    {
        OutputKnob.SetPosition(FirstKnobPosition);

        GUILayout.BeginVertical(BoxStyle);

        KnobValueField(AverageKnob, ref Average);
        KnobValueField(DeviationKnob, ref Deviation);

        GUILayout.EndVertical();

        if (GUI.changed)
            canvas.OnNodeChange(this);
    }

    public override void FillNodeActionsMenu(NodeEditorInputInfo inputInfo, GenericMenu menu)
    {
        base.FillNodeActionsMenu(inputInfo, menu);
        menu.AddSeparator("");

        if (!DynamicSeed)
        {
            menu.AddItem(new GUIContent("Enable dynamic seed"), false, () =>
            {
                DynamicSeed = true;
                canvas.OnNodeChange(this);
            });
        }
        else
        {
            menu.AddItem(new GUIContent("Disable dynamic seed"), false, () =>
            {
                DynamicSeed = false;
                canvas.OnNodeChange(this);
            });
        }
    }

    public override void RefreshPreview()
    {
        var avg = GetIfConnected<double>(AverageKnob);
        var dev = GetIfConnected<double>(DeviationKnob);

        avg?.ResetState();
        dev?.ResetState();

        if (avg != null) Average = avg.Get();
        if (dev != null) Deviation = dev.Get();
    }

    public override bool Calculate()
    {
        OutputKnob.SetValue<ISupplier<double>>(new Output(
            SupplierOrFallback(AverageKnob, Average),
            SupplierOrFallback(DeviationKnob, Deviation),
            CombinedSeed, DynamicSeed, TerrainCanvas.CreateRandomInstance()
        ));
        return true;
    }

    private class Output : ISupplier<double>
    {
        private readonly ISupplier<double> _average;
        private readonly ISupplier<double> _deviation;
        private readonly int _seed;
        private readonly bool _dynamicSeed;
        private readonly IRandom _random;

        public Output(ISupplier<double> average, ISupplier<double> deviation, int seed, bool dynamicSeed, IRandom random)
        {
            _average = average;
            _deviation = deviation;
            _seed = seed;
            _dynamicSeed = dynamicSeed;
            _random = random;
            _random.Reinitialise(_seed);
        }

        public double Get()
        {
            if (!_dynamicSeed) _random.Reinitialise(_seed);
            double average = _average.Get();
            double deviation = _deviation.Get();
            return _random.NextDouble(average - deviation, average + deviation);
        }

        public void ResetState()
        {
            _random.Reinitialise(_seed);
            _average.ResetState();
            _deviation.ResetState();
        }
    }
}
