using UnityEngine;

namespace TerrainGraph;

public class BasicPreviewScheduler : IPreviewScheduler
{
    public static readonly IPreviewScheduler Instance = new BasicPreviewScheduler();

    public void ScheduleTask(PreviewTask task)
    {
        task.Task.Invoke();
        task.OnFinished.Invoke();
    }

    public void DrawLoadingIndicator(NodeBase node, Rect rect) { }
}
