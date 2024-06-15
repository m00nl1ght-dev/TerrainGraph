using System;
using System.Collections.Generic;
using System.Linq;
using NodeEditorFramework;
using NodeEditorFramework.Utilities;
using TerrainGraph.Util;
using UnityEngine;

namespace TerrainGraph;

[HotSwappable]
[Serializable]
[Node(false, "Curve/Preview", 185)]
public class NodeCurvePreview : NodeBase
{
    public const string ID = "curvePreview";
    public override string GetID => ID;

    public override string Title => "Preview";
    public override Vector2 DefaultSize => new(_previewSize, _previewSize + (_changingViewport ? 85 : 20));
    public override bool AutoLayout => false;

    [ValueConnectionKnob("Input", Direction.In, CurveFunctionConnection.Id)]
    public ValueConnectionKnob InputKnob;

    [ValueConnectionKnob("Output", Direction.Out, CurveFunctionConnection.Id)]
    public ValueConnectionKnob OutputKnob;

    public string PreviewModelId = "Default";

    public double ViewportMinX;
    public double ViewportMinY;
    public double ViewportMaxX = 1;
    public double ViewportMaxY = 1;

    [NonSerialized]
    private int _previewSize = 100;

    [NonSerialized]
    private Color[] _previewBuffer;

    [NonSerialized]
    private Texture2D _previewTexture;

    [NonSerialized]
    private ICurveFunction<double> _previewFunction;

    [NonSerialized]
    private bool _changingViewport;

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

            if (Event.current.type == EventType.Repaint && _previewFunction != null)
            {
                ActiveTooltipHandler?.Invoke(pRect, () =>
                {
                    var posInNode = NodeEditor.ScreenToCanvasSpace(Event.current.mousePosition) - rect.min - contentOffset;
                    var posInPreview = new Vector2Int((int) posInNode.x, TerrainCanvas.GridPreviewSize - (int) posInNode.y);

                    var posX = (posInPreview.x / (double) _previewSize).Lerp(ViewportMinX, ViewportMaxX);
                    var value = _previewFunction.ValueAt(posX);

                    return Math.Round(value, 2) + " ( " + Math.Round(posX, 2) + " )";
                }, 0f);
            }
        }

        if (OngoingPreviewTask != null)
        {
            TerrainCanvas.PreviewScheduler.DrawLoadingIndicator(this, pRect);
        }

        if (_changingViewport)
        {
            GUILayout.BeginVertical(BoxStyle);

            GUILayout.BeginHorizontal(BoxStyle);
            ViewportMinX = RTEditorGUI.FloatField(GUIContent.none, (float) ViewportMinX, BoxLayout);
            GUILayout.FlexibleSpace();
            ViewportMaxX = RTEditorGUI.FloatField(GUIContent.none, (float) ViewportMaxX, BoxLayout);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(BoxStyle);
            ViewportMinY = RTEditorGUI.FloatField(GUIContent.none, (float) ViewportMinY, BoxLayout);
            GUILayout.FlexibleSpace();
            ViewportMaxY = RTEditorGUI.FloatField(GUIContent.none, (float) ViewportMaxY, BoxLayout);
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();

            if (GUI.changed)
                canvas.OnNodeChange(this);
        }
    }

    public override void FillNodeActionsMenu(NodeEditorInputInfo inputInfo, GenericMenu menu)
    {
        base.FillNodeActionsMenu(inputInfo, menu);
        menu.AddSeparator("");

        SelectionMenu(menu, PreviewModelIds.ToList(), s => PreviewModelId = s, e => "Set preview model/" + e);

        menu.AddItem(new GUIContent(_changingViewport ? "Finish viewport" : "Edit viewport"), false, () =>
        {
            _changingViewport = !_changingViewport;
        });
    }

    public override bool Calculate()
    {
        OutputKnob.SetValue(InputKnob.GetValue<ISupplier<ICurveFunction<double>>>());
        return true;
    }

    public override void RefreshPreview()
    {
        var previewSize = _previewSize;
        var previewBuffer = _previewBuffer;

        var previewModel = GetPreviewModel(PreviewModelId);

        _previewTexture.SetPixels(previewBuffer);
        _previewTexture.Apply();

        var supplier = SupplierOrFallback(InputKnob, CurveFunction.Zero);

        TerrainCanvas.PreviewScheduler.ScheduleTask(new PreviewTask(this, () =>
        {
            var previewFunction = _previewFunction = supplier.ResetAndGet();

            for (int x = 0; x < previewSize; x++)
            {
                var posX = (x / (double) previewSize).Lerp(ViewportMinX, ViewportMaxX);

                for (int y = 0; y < previewSize; y++)
                {
                    var val = previewFunction.ValueAt(posX);
                    var posY = (y / (double) previewSize).Lerp(ViewportMinY, ViewportMaxY);

                    previewBuffer[y * previewSize + x] = previewModel.GetColorFor(this, val, posX, posY);
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

    public static IEnumerable<string> PreviewModelIds => PreviewModels.Keys.AsEnumerable();

    public static readonly IPreviewModel DefaultModel = new DefaultPreviewModel();

    public static void RegisterPreviewModel(IPreviewModel model, string id)
    {
        PreviewModels[id] = model;
    }

    public static IPreviewModel GetPreviewModel(string id)
    {
        return PreviewModels.TryGetValue(id, out var previewModel) ? previewModel : DefaultModel;
    }

    static NodeCurvePreview()
    {
        RegisterPreviewModel(DefaultModel, "Default");
    }

    public interface IPreviewModel
    {
        public Color GetColorFor(NodeCurvePreview node, double val, double posX, double posY);
    }

    private class DefaultPreviewModel : IPreviewModel
    {
        public Color GetColorFor(NodeCurvePreview node, double val, double posX, double posY)
        {
            if (posX == 0 && node.ViewportMinX != 0 && node.ViewportMaxX != 0) return Color.blue;
            if (posY == 0 && node.ViewportMinY != 0 && node.ViewportMaxY != 0) return Color.blue;
            if (posY >= 0f) return val >= posY ? Color.white : Color.black;
            return val <= posY ? Color.white : Color.black;
        }
    }
}
