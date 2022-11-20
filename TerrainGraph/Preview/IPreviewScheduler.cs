using UnityEngine;

namespace TerrainGraph;

public interface IPreviewScheduler
{
    public void ScheduleTask(PreviewTask task);

    public void DrawLoadingIndicator(NodeBase node, Rect rect);
}