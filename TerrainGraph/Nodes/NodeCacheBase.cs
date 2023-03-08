using System;
using NodeEditorFramework;
using UnityEngine;

namespace TerrainGraph;

[Serializable]
public abstract class NodeCacheBase : NodeBase
{
    public override Vector2 DefaultSize => new(60, 55);
    public override bool AutoLayout => false;

    public override string Title => "Cache";

    public abstract ValueConnectionKnob InputKnobRef { get; }
    public abstract ValueConnectionKnob OutputKnobRef { get; }

    public override void NodeGUI()
    {
        InputKnobRef.SetPosition(FirstKnobPosition);
        OutputKnobRef.SetPosition(FirstKnobPosition);

        GUILayout.BeginVertical(BoxStyle);

        GUILayout.BeginHorizontal(BoxStyle);
        GUILayout.Label("", BoxLayout);
        GUILayout.EndHorizontal();

        GUILayout.EndVertical();
    }

    protected class Output<T> : ISupplier<T>
    {
        private readonly ISupplier<T> _input;

        private bool _cached;
        private T _cache;

        public Output(ISupplier<T> input)
        {
            _input = input;
        }

        public T Get()
        {
            if (!_cached)
            {
                _cache = _input.Get();
                _cached = true;
            }

            return _cache;
        }

        public void ResetState()
        {
            _input.ResetState();
        }
    }
}
