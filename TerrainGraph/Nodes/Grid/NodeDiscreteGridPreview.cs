using System;
using System.Linq;
using NodeEditorFramework;
using NodeEditorFramework.Utilities;
using UnityEngine;

namespace TerrainGraph;

[Serializable]
public abstract class NodeDiscreteGridPreview<T> : NodeBase
{
    public override Vector2 DefaultSize => new(PreviewSize, PreviewSize + 20);
    public override bool AutoLayout => false;

    public abstract ValueConnectionKnob InputKnobRef { get; }
    public abstract ValueConnectionKnob OutputKnobRef { get; }

    public string PreviewTransformId = "Default";

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
                    var posInNode = NodeEditor.ScreenToCanvasSpace(Event.current.mousePosition) - rect.min - contentOffset;
                    var posInPreview = new Vector2Int((int) posInNode.x, TerrainCanvas.GridPreviewSize - (int) posInNode.y);

                    var previewTransform = NodeGridPreview.GetPreviewTransform(PreviewTransformId);
                    var canvasPos = previewTransform.PreviewToCanvasSpace(TerrainCanvas, posInPreview);
                    var value = PreviewFunction == null ? default : PreviewFunction.ValueAt(canvasPos.x, canvasPos.y);
                    return MakeTooltip(value, canvasPos.x, canvasPos.y);
                }, 0f);
            }
        }

        if (OngoingPreviewTask != null)
        {
            TerrainCanvas.PreviewScheduler.DrawLoadingIndicator(this, pRect);
        }
    }

    public override void FillNodeActionsMenu(NodeEditorInputInfo inputInfo, GenericMenu menu)
    {
        base.FillNodeActionsMenu(inputInfo, menu);
        menu.AddSeparator("");

        SelectionMenu(menu, NodeGridPreview.PreviewTransformIds.ToList(), s => PreviewTransformId = s, e => "Set preview transform/" + e);
    }

    protected abstract string MakeTooltip(T value, double x, double y);

    protected abstract Color GetColor(T value);

    protected abstract IGridFunction<T> Default { get; }

    public override void RefreshPreview()
    {
        var previewSize = PreviewSize;
        var previewBuffer = PreviewBuffer;

        var previewTransform = NodeGridPreview.GetPreviewTransform(PreviewTransformId);

        var supplier = SupplierOrFallback(InputKnobRef, Default);

        TerrainCanvas.PreviewScheduler.ScheduleTask(new PreviewTask(this, () =>
        {
            var previewFunction = PreviewFunction = supplier.ResetAndGet();

            for (int x = 0; x < previewSize; x++)
            {
                for (int y = 0; y < previewSize; y++)
                {
                    var pos = previewTransform.PreviewToCanvasSpace(TerrainCanvas, new Vector2Int(x, y));
                    var color = GetColor(previewFunction.ValueAt(pos.x, pos.y));
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
