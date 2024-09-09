using System;
using System.Collections.Generic;
using System.Linq;
using NodeEditorFramework;
using TerrainGraph.Util;

namespace TerrainGraph;

#if DEBUG
public class HotSwappableAttribute : Attribute;
#endif

[Serializable]
[NodeCanvasType("TerrainCanvas")]
public class TerrainCanvas : NodeCanvas
{
    public IEnumerable<NodeBase> Nodes => nodes.Where(n => n is NodeBase).Cast<NodeBase>();
    public TerrainCanvasTraversal Calculator => (TerrainCanvasTraversal) Traversal;

    public override string canvasName => "TerrainGraph";

    public virtual int GridFullSize => 100;
    public virtual int GridPathSize => 100;
    public virtual int GridPreviewSize => 100;

    public double GridPreviewRatio => (double) GridFullSize / GridPreviewSize;

    public int RandSeed = NodeBase.SeedSource.Next();
    public bool HasActiveGUI { get; private set; }

    public virtual IPreviewScheduler PreviewScheduler => BasicPreviewScheduler.Instance;

    internal List<NodeInputNamed> NamedInputs = [];
    internal List<NodeOutputNamed> NamedOutputs = [];

    internal NodeInputNamed FindNamedInput(Type type, string inputName) =>
        NamedInputs.FirstOrDefault(o => o.Name == inputName && o.ValueKnob?.valueType == type);

    internal NodeOutputNamed FindNamedOutput(Type type, string outputName) =>
        NamedOutputs.FirstOrDefault(o => o.Name == outputName && o.ValueKnob?.valueType == type);

    public void SetNamedInput<T>(string inputName, ISupplier<T> supplier) =>
        FindNamedInput(typeof(T), inputName)?.Set(supplier);

    public ISupplier<T> GetNamedOutput<T>(string outputName) =>
        FindNamedOutput(typeof(T), outputName)?.Get<T>();

    public void SetNamedInputs(IEnumerable<TerrainCanvas> stack)
    {
        NodeOutputNamed FindCounterpart(TerrainCanvas canvas, NodeInputNamed input) =>
            canvas.FindNamedOutput(input.ValueKnob.valueType, input.Name);

        foreach (var namedInput in NamedInputs)
        {
            var output = stack.Select(c => FindCounterpart(c, namedInput)).LastOrDefault(s => s != null);
            if (output != null) namedInput.ValueSupplier = output.ValueKnob.GetValue();
        }
    }

    public void ClearNamedInputs()
    {
        foreach (var namedInput in NamedInputs)
        {
            namedInput.ValueSupplier = null;
        }
    }

    protected override void OnCreate()
    {
        ValidateSelf();
    }

    protected override void ValidateSelf()
    {
        Traversal ??= new TerrainCanvasTraversal(this);
    }

    public virtual IRandom CreateRandomInstance()
    {
        return new FastRandom(RandSeed);
    }

    public virtual void RefreshPreviews()
    {
        foreach (var nodeBase in Nodes) nodeBase.RefreshPreview();
    }

    public virtual void PrepareGUI()
    {
        HasActiveGUI = true;
        foreach (var nodeBase in Nodes) nodeBase.PrepareGUI();
    }

    public virtual void CleanUpGUI()
    {
        foreach (var nodeBase in Nodes) nodeBase.CleanUpGUI();
        HasActiveGUI = false;
    }

    public virtual void ResetView()
    {
        ValidateSelf();
    }
}
