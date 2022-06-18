using System;
using NodeEditorFramework;
using UnityEngine;

namespace TerrainGraph;

[Serializable]
public abstract class NodeDiscreteGridPreview<T> : NodeBase
{
    public int PreviewSize => TerrainCanvas?.GridPreviewSize ?? 100;
    public override Vector2 DefaultSize => new(PreviewSize, PreviewSize + 20);
    public override bool AutoLayout => false;
    
    public abstract ValueConnectionKnob InputKnobRef { get; }
    public abstract ValueConnectionKnob OutputKnobRef { get; }
    
    [NonSerialized]
    protected Texture2D PreviewTexture;

    [NonSerialized] 
    protected IGridFunction<T> PreviewFunction;

    public override void PrepareGUI()
    {
        PreviewTexture = new Texture2D(PreviewSize, PreviewSize, TextureFormat.RGB24, false);
    }

    public override void CleanUpGUI()
    {
        if (PreviewTexture != null) Destroy(PreviewTexture);
        PreviewTexture = null;
        PreviewFunction = null;
    }

    public override void NodeGUI()
    {
        InputKnobRef.SetPosition(FirstKnobPosition);
        OutputKnobRef.SetPosition(FirstKnobPosition);

        if (PreviewTexture != null)
        {
            Rect pRect = GUILayoutUtility.GetRect(PreviewSize, PreviewSize);
            GUI.DrawTexture(pRect, PreviewTexture);
            
            if (Event.current.type == EventType.Repaint)
            {
                ActiveTooltipHandler?.Invoke(pRect, () =>
                {
                    Vector2 pos = NodeEditor.ScreenToCanvasSpace(Event.current.mousePosition) - rect.min - contentOffset;
                    double previewRatio = TerrainCanvas.GridPreviewRatio;
                    
                    double x = Math.Max(0, Math.Min(PreviewSize, pos.x)) * previewRatio;
                    double y = GridSize - Math.Max(0, Math.Min(PreviewSize, pos.y)) * previewRatio;

                    T value = PreviewFunction == null ? default : PreviewFunction.ValueAt(x, y);
                    return MakeTooltip(value, x, y);
                }, 0f);
            }
        }
    }

    protected abstract string MakeTooltip(T value, double x, double y);

    protected abstract Color GetColor(T value);

    protected abstract IGridFunction<T> Default { get; }

    public override void RefreshPreview()
    {
        var previewRatio = TerrainCanvas.GridPreviewRatio;
        PreviewFunction = InputKnobRef.connected() ? InputKnobRef.GetValue<ISupplier<IGridFunction<T>>>().ResetAndGet() : Default;
        
        for (int x = 0; x < TerrainCanvas.GridPreviewSize; x++)
        {
            for (int y = 0; y < TerrainCanvas.GridPreviewSize; y++)
            {
                Color color = GetColor(PreviewFunction.ValueAt(x * previewRatio, y * previewRatio));
                PreviewTexture.SetPixel(x, y, color);
            }
        }
        
        PreviewTexture.Apply();
    }
}