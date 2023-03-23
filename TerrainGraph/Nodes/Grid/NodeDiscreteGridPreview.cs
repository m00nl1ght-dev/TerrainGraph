using System;
using NodeEditorFramework;
using UnityEngine;

namespace TerrainGraph;

[Serializable]
public abstract class NodeDiscreteGridPreview<T> : NodeBase
{
    public override Vector2 DefaultSize => new(PreviewSize, PreviewSize + 20);
    public override bool AutoLayout => false;

    public abstract ValueConnectionKnob InputKnobRef { get; }
    public abstract ValueConnectionKnob OutputKnobRef { get; }

    [NonSerialized]
    protected int PreviewSize = 100;

    [NonSerialized]
    protected Color[] PreviewBuffer;

    [NonSerialized]
    protected Texture2D PreviewTexture;

    [NonSerialized]
    protected IGridFunction<T> PreviewFunction;

    public override void PrepareGUI()
    {
        PreviewSize = TerrainCanvas?.GridPreviewSize ?? 100;
        PreviewBuffer = new Color[PreviewSize * PreviewSize];
        PreviewTexture = new Texture2D(PreviewSize, PreviewSize, TextureFormat.RGB24, false);
    }

    public override void CleanUpGUI()
    {
        if (PreviewTexture != null) Destroy(PreviewTexture);
        PreviewTexture = null;
        PreviewBuffer = null;
        PreviewFunction = null;
    }

    public override void NodeGUI()
    {
        InputKnobRef.SetPosition(FirstKnobPosition);
        OutputKnobRef.SetPosition(FirstKnobPosition);

        var pRect = GUILayoutUtility.GetRect(PreviewSize, PreviewSize);

        if (PreviewTexture != null)
        {
            GUI.DrawTexture(pRect, PreviewTexture);

            if (Event.current.type == EventType.Repaint)
            {
                ActiveTooltipHandler?.Invoke(pRect, () =>
                {
                    var pos = NodeEditor.ScreenToCanvasSpace(Event.current.mousePosition) - rect.min - contentOffset;
                    double previewRatio = TerrainCanvas.GridPreviewRatio;

                    double x = Math.Max(0, Math.Min(PreviewSize, pos.x)) * previewRatio;
                    double y = GridSize - Math.Max(0, Math.Min(PreviewSize, pos.y)) * previewRatio;

                    var value = PreviewFunction == null ? default : PreviewFunction.ValueAt(x, y);
                    return MakeTooltip(value, x, y);
                }, 0f);
            }
        }

        if (OngoingPreviewTask != null)
        {
            TerrainCanvas.PreviewScheduler.DrawLoadingIndicator(this, pRect);
        }
    }

    protected abstract string MakeTooltip(T value, double x, double y);

    protected abstract Color GetColor(T value);

    protected abstract IGridFunction<T> Default { get; }

    public override void RefreshPreview()
    {
        var previewSize = PreviewSize;
        var previewBuffer = PreviewBuffer;
        var previewRatio = TerrainCanvas.GridPreviewRatio;
        var supplier = InputKnobRef.connected() ? InputKnobRef.GetValue<ISupplier<IGridFunction<T>>>() : null;

        TerrainCanvas.PreviewScheduler.ScheduleTask(new PreviewTask(this, () =>
        {
            var previewFunction = PreviewFunction = supplier != null ? supplier.ResetAndGet() : Default;

            for (int x = 0; x < previewSize; x++)
            {
                for (int y = 0; y < previewSize; y++)
                {
                    var color = GetColor(previewFunction.ValueAt(x * previewRatio, y * previewRatio));
                    previewBuffer[y * previewSize + x] = color;
                }
            }
        }, () =>
        {
            if (PreviewTexture != null)
            {
                PreviewTexture.SetPixels(previewBuffer);
                PreviewTexture.Apply();
            }
        }));
    }
}
