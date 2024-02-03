using System;
using NodeEditorFramework;
using TerrainGraph.Flow;
using UnityEngine;

namespace TerrainGraph;

[Serializable]
[Node(false, "Path/Preview", 620)]
public class NodePathPreview : NodeBase
{
    public const string ID = "pathPreview";
    public override string GetID => ID;

    public override string Title => "Preview";
    public override Vector2 DefaultSize => new(_previewSize, _previewSize + 20);
    public override bool AutoLayout => false;

    [ValueConnectionKnob("Input", Direction.In, PathFunctionConnection.Id)]
    public ValueConnectionKnob InputKnob;

    [ValueConnectionKnob("Output", Direction.Out, PathFunctionConnection.Id)]
    public ValueConnectionKnob OutputKnob;

    [NonSerialized]
    private int _previewSize = 100;

    [NonSerialized]
    private Color[] _previewBuffer;

    [NonSerialized]
    private Texture2D _previewTexture;

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
    }

    public override void NodeGUI()
    {
        InputKnob.SetPosition(FirstKnobPosition);
        OutputKnob.SetPosition(FirstKnobPosition);

        var pRect = GUILayoutUtility.GetRect(_previewSize, _previewSize);

        if (_previewTexture != null)
        {
            GUI.DrawTexture(pRect, _previewTexture);
        }

        if (OngoingPreviewTask != null)
        {
            TerrainCanvas.PreviewScheduler.DrawLoadingIndicator(this, pRect);
        }
    }

    public override bool Calculate()
    {
        OutputKnob.SetValue(InputKnob.GetValue<ISupplier<Path>>());
        return true;
    }

    public override void RefreshPreview()
    {
        var previewSize = _previewSize;
        var previewBuffer = _previewBuffer;
        var previewRatio = TerrainCanvas.GridPreviewRatio;

        var model = NodeGridPreview.DefaultModel;

        _previewTexture.SetPixels(previewBuffer);
        _previewTexture.Apply();

        var supplier = SupplierOrFallback(InputKnob, Path.Empty);

        TerrainCanvas.PreviewScheduler.ScheduleTask(new PreviewTask(this, () =>
        {
            var path = supplier.ResetAndGet();

            var tracer = new PathTracer(
                TerrainCanvas.GridFullSize,
                TerrainCanvas.GridFullSize,
                NodePathTrace.GridMarginDefault,
                NodePathTrace.TraceMarginInnerDefault,
                NodePathTrace.TraceMarginOuterDefault
            );

            tracer.Trace(path);

            var previewFunction = tracer.MainGrid;

            for (int x = 0; x < previewSize; x++)
            {
                for (int y = 0; y < previewSize; y++)
                {
                    var val = (float) previewFunction.ValueAt(x * previewRatio, y * previewRatio);
                    var color = model.GetColorFor(val, x, y);
                    previewBuffer[y * previewSize + x] = color;
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
}
