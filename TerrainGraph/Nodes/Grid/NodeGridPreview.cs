using System;
using System.Collections.Generic;
using System.Linq;
using NodeEditorFramework;
using NodeEditorFramework.Utilities;
using UnityEngine;

namespace TerrainGraph;

[Serializable]
[Node(false, "Grid/Preview", 215)]
public class NodeGridPreview : NodeBase
{
    public const string ID = "gridPreview";
    public override string GetID => ID;

    public override string Title => "Preview";
    public override Vector2 DefaultSize => new(_previewSize, _previewSize + 20);
    public override bool AutoLayout => false;

    [ValueConnectionKnob("Input", Direction.In, GridFunctionConnection.Id)]
    public ValueConnectionKnob InputKnob;

    [ValueConnectionKnob("Output", Direction.Out, GridFunctionConnection.Id)]
    public ValueConnectionKnob OutputKnob;

    public string PreviewModelId = "Default";
    public string PreviewTransformId = "Default";

    [NonSerialized]
    private int _previewSize = 100;

    [NonSerialized]
    private Color[] _previewBuffer;

    [NonSerialized]
    private Texture2D _previewTexture;

    [NonSerialized]
    private IGridFunction<double> _previewFunction;

    public override void PrepareGUI()
    {
        _previewSize = TerrainCanvas?.GridPreviewSize ?? 100;
        _previewBuffer = new Color[_previewSize * _previewSize];
        _previewTexture = new Texture2D(_previewSize, _previewSize, TextureFormat.RGB24, false);
    }

    public override void CleanUpGUI()
    {
        if (_previewTexture != null) Destroy(_previewTexture);
        _previewTexture = null;
        _previewBuffer = null;
        _previewFunction = null;
    }

    public override void NodeGUI()
    {
        InputKnob.SetPosition(FirstKnobPosition);
        OutputKnob.SetPosition(FirstKnobPosition);

        var pRect = GUILayoutUtility.GetRect(_previewSize, _previewSize);

        if (_previewTexture != null)
        {
            GUI.DrawTexture(pRect, _previewTexture);

            if (Event.current.type == EventType.Repaint)
            {
                ActiveTooltipHandler?.Invoke(pRect, () =>
                {
                    var posInNode = NodeEditor.ScreenToCanvasSpace(Event.current.mousePosition) - rect.min - contentOffset;
                    var posInPreview = new Vector2Int((int) posInNode.x, TerrainCanvas.GridPreviewSize - (int) posInNode.y);

                    var previewTransform = GetPreviewTransform(PreviewTransformId);
                    var canvasPos = previewTransform.PreviewToCanvasSpace(TerrainCanvas, posInPreview);
                    var value = _previewFunction?.ValueAt(canvasPos.x, canvasPos.y) ?? 0;

                    return Math.Round(value, 2) + " ( " + Math.Round(canvasPos.x, 0) + " | " + Math.Round(canvasPos.y, 0) + " )";
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

        SelectionMenu(menu, PreviewModelIds.ToList(), SetModel, e => "Set preview model/" + e);
        SelectionMenu(menu, PreviewTransformIds.ToList(), SetTransform, e => "Set preview transform/" + e);
    }

    private void SetModel(string id)
    {
        PreviewModelId = id;
        canvas.OnNodeChange(this);
    }

    private void SetTransform(string id)
    {
        PreviewTransformId = id;
        canvas.OnNodeChange(this);
    }

    public override bool Calculate()
    {
        OutputKnob.SetValue(InputKnob.GetValue<ISupplier<IGridFunction<double>>>());
        return true;
    }

    public override void RefreshPreview()
    {
        var previewSize = _previewSize;
        var previewBuffer = _previewBuffer;

        var previewModel = GetPreviewModel(PreviewModelId);
        var previewTransform = GetPreviewTransform(PreviewTransformId);

        _previewTexture.SetPixels(previewBuffer);
        _previewTexture.Apply();

        var supplier = SupplierOrFallback(InputKnob, GridFunction.Zero);

        TerrainCanvas.PreviewScheduler.ScheduleTask(new PreviewTask(this, () =>
        {
            var previewFunction = _previewFunction = supplier.ResetAndGet();

            for (int x = 0; x < previewSize; x++)
            {
                for (int y = 0; y < previewSize; y++)
                {
                    var pos = previewTransform.PreviewToCanvasSpace(TerrainCanvas, new Vector2Int(x, y));
                    var val = (float) previewFunction.ValueAt(pos.x, pos.y);
                    previewBuffer[y * previewSize + x] = previewModel.GetColorFor(val, x, y);
                }
            }
        }, () =>
        {
            if (_previewTexture != null)
            {
                _previewTexture.SetPixels(previewBuffer);
                _previewTexture.Apply();
            }
        }));
    }

    private static readonly Dictionary<string, IPreviewModel> PreviewModels = new();
    private static readonly Dictionary<string, IPreviewTransform> PreviewTransforms = new();

    public static IEnumerable<string> PreviewModelIds => PreviewModels.Keys.AsEnumerable();
    public static IEnumerable<string> PreviewTransformIds => PreviewTransforms.Keys.AsEnumerable();

    public static readonly IPreviewModel DefaultModel = new DefaultPreviewModel();
    public static readonly IPreviewModel DefaultModel_10 = new DefaultPreviewModel(10f);
    public static readonly IPreviewModel DefaultModel_100 = new DefaultPreviewModel(100f);

    public static readonly IPreviewTransform DefaultTransform = new DefaultPreviewTransform(1f, 0f);
    public static readonly IPreviewTransform DefaultTransformQuad = new DefaultPreviewTransform(1f, -0.5f);
    public static readonly IPreviewTransform LargeTransform = new DefaultPreviewTransform(2f, -0.5f);
    public static readonly IPreviewTransform LargeTransformQuad = new DefaultPreviewTransform(2f, -1f);
    public static readonly IPreviewTransform FixedTransform1X100 = new FixedPreviewTransform(new Vector2(1f, 100f));
    public static readonly IPreviewTransform FixedTransform100X1 = new FixedPreviewTransform(new Vector2(100f, 1f));

    public static void RegisterPreviewModel(IPreviewModel model, string id)
    {
        PreviewModels[id] = model;
    }

    public static void RegisterPreviewRange(IPreviewTransform transform, string id)
    {
        PreviewTransforms[id] = transform;
    }

    public static IPreviewModel GetPreviewModel(string id)
    {
        return PreviewModels.TryGetValue(id, out var previewModel) ? previewModel : DefaultModel;
    }

    public static IPreviewTransform GetPreviewTransform(string id)
    {
        return PreviewTransforms.TryGetValue(id, out var previewTransform) ? previewTransform : DefaultTransform;
    }

    static NodeGridPreview()
    {
        RegisterPreviewModel(DefaultModel, "Default");
        RegisterPreviewModel(DefaultModel_10, "Default x10");
        RegisterPreviewModel(DefaultModel_100, "Default x100");

        RegisterPreviewRange(DefaultTransform, "Default");
        RegisterPreviewRange(DefaultTransformQuad, "Default Quad");
        RegisterPreviewRange(LargeTransform, "Large");
        RegisterPreviewRange(LargeTransformQuad, "Large Quad");
        RegisterPreviewRange(FixedTransform1X100, "Fixed 1 x 100");
        RegisterPreviewRange(FixedTransform100X1, "Fixed 100 x 1");
    }

    public interface IPreviewModel
    {
        public Color GetColorFor(float val, int x, int y);
    }

    public interface IPreviewTransform
    {
        public Vector2Int CanvasToPreviewSpace(TerrainCanvas canvas, Vector2 pos);
        public Vector2 PreviewToCanvasSpace(TerrainCanvas canvas, Vector2Int pos);
    }

    private class DefaultPreviewModel : IPreviewModel
    {
        public readonly float Multiplier;

        public DefaultPreviewModel(float multiplier = 1f)
        {
            Multiplier = multiplier;
        }

        public Color GetColorFor(float val, int x, int y)
        {
            val /= Multiplier;
            return val switch
            {
                < -5f => new Color(0f, 0.5f, 0f),
                < -1f => new Color(0f, -(val + 1f) / 8f, 0.5f + (val + 1f) / 8f),
                < 0f => new Color(0f, 0f, -val / 2f),
                < 1f => new Color(val, val, val),
                < 2f => new Color(1f, 1f, 2f - val),
                < 5f => new Color(1f, 1f - (val - 2f) / 3f, 0f),
                _ => new Color(1f, 0f, 0f)
            };
        }
    }

    private class DefaultPreviewTransform : IPreviewTransform
    {
        public readonly float Scale;
        public readonly float Offset;

        public DefaultPreviewTransform(float scale, float offset)
        {
            Scale = scale;
            Offset = offset;
        }

        public Vector2Int CanvasToPreviewSpace(TerrainCanvas canvas, Vector2 pos)
        {
            var f = canvas.GridFullSize / (double) canvas.GridPreviewSize;
            var x = (pos.x - canvas.GridFullSize * Offset) / (f * Scale);
            var y = (pos.y - canvas.GridFullSize * Offset) / (f * Scale);
            return new Vector2Int((int) x, (int) y);
        }

        public Vector2 PreviewToCanvasSpace(TerrainCanvas canvas, Vector2Int pos)
        {
            var f = canvas.GridFullSize / (float) canvas.GridPreviewSize;
            var x = pos.x * f * Scale + canvas.GridFullSize * Offset;
            var y = pos.y * f * Scale + canvas.GridFullSize * Offset;
            return new Vector2(x, y);
        }
    }

    private class FixedPreviewTransform : IPreviewTransform
    {
        public readonly Vector2 Size;
        public readonly Vector2 Offset;

        public FixedPreviewTransform(Vector2 size, Vector2 offset = default)
        {
            Size = size;
            Offset = offset;
        }

        public Vector2Int CanvasToPreviewSpace(TerrainCanvas canvas, Vector2 pos)
        {
            var x = (pos.x / Size.x - Offset.x * Size.x) * canvas.GridPreviewSize;
            var y = (pos.y / Size.y - Offset.y * Size.y) * canvas.GridPreviewSize;
            return new Vector2Int((int) x, (int) y);
        }

        public Vector2 PreviewToCanvasSpace(TerrainCanvas canvas, Vector2Int pos)
        {
            var x = (pos.x / (float) canvas.GridPreviewSize + Offset.x) * Size.x;
            var y = (pos.y / (float) canvas.GridPreviewSize + Offset.y) * Size.y;
            return new Vector2(x, y);
        }
    }
}
