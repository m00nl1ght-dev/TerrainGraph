using System;
using System.Collections.Generic;
using System.Linq;
using NodeEditorFramework;
using TerrainGraph.Util;

namespace TerrainGraph;

[Serializable]
[NodeCanvasType("TerrainCanvas")]
public class TerrainCanvas : NodeCanvas
{
    public IEnumerable<NodeBase> Nodes => nodes.Where(n => n is NodeBase).Cast<NodeBase>();
    public TerrainCanvasTraversal Calculator => (TerrainCanvasTraversal) Traversal;

    public override string canvasName => "TerrainGraph";

    public virtual int GridFullSize => 100;
    public virtual int GridPreviewSize => 100;
    public double GridPreviewRatio => (double) GridFullSize / GridPreviewSize;

    public int RandSeed = NodeBase.SeedSource.Next();
    public bool HasActiveGUI { get; private set; }

    public virtual IPreviewScheduler PreviewScheduler => BasicPreviewScheduler.Instance;

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
