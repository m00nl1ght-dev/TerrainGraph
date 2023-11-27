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
                    var pos = NodeEditor.ScreenToCanvasSpace(Event.current.mousePosition) - rect.min - contentOffset;
                    double previewRatio = TerrainCanvas.GridPreviewRatio;

                    double x = Math.Max(0, Math.Min(_previewSize, pos.x)) * previewRatio;
                    double y = GridSize - Math.Max(0, Math.Min(_previewSize, pos.y)) * previewRatio;

                    double value = _previewFunction?.ValueAt(x, y) ?? 0;
                    return Math.Round(value, 2) + " ( " + Math.Round(x, 0) + " | " + Math.Round(y, 0) + " )";
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

        SelectionMenu(menu, PreviewModels.Keys.ToList(), SetModel, e => "Set preview model/" + e);
    }

    private void SetModel(string id)
    {
        PreviewModelId = id;
        canvas.OnNodeChange(this);
    }

    public override bool Calculate()
    {
        OutputKnob.SetValue(InputKnob.GetValue<ISupplier<IGridFunction<double>>>()); // TODO doesn't update when input knob disconnected?
        return true;
    }

    public override void RefreshPreview()
    {
        var previewSize = _previewSize;
        var previewBuffer = _previewBuffer;
        var previewRatio = TerrainCanvas.GridPreviewRatio;

        PreviewModels.TryGetValue(PreviewModelId, out var previewModel);
        previewModel ??= DefaultModel;

        _previewTexture.SetPixels(previewBuffer);
        _previewTexture.Apply();

        var supplier = InputKnob.connected() ? InputKnob.GetValue<ISupplier<IGridFunction<double>>>() : null;

        TerrainCanvas.PreviewScheduler.ScheduleTask(new PreviewTask(this, () =>
        {
            var previewFunction = _previewFunction = supplier != null ? supplier.ResetAndGet() : GridFunction.Zero;

            for (int x = 0; x < previewSize; x++)
            {
                for (int y = 0; y < previewSize; y++)
                {
                    var val = (float) previewFunction.ValueAt(x * previewRatio, y * previewRatio);
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
    public static readonly IPreviewModel DefaultModel = new DefaultPreviewModel();
    public static readonly IPreviewModel DefaultModel_10 = new DefaultPreviewModel(10f);
    public static readonly IPreviewModel DefaultModel_100 = new DefaultPreviewModel(100f);

    public static void RegisterPreviewModel(IPreviewModel model, string id)
    {
        PreviewModels[id] = model;
    }

    static NodeGridPreview()
    {
        RegisterPreviewModel(DefaultModel, "Default");
        RegisterPreviewModel(DefaultModel_10, "Default x10");
        RegisterPreviewModel(DefaultModel_100, "Default x100");
    }

    public interface IPreviewModel
    {
        public Color GetColorFor(float val, int x, int y);
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
}
