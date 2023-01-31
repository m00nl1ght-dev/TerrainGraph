using System;
using UnityEngine;

namespace TerrainGraph;

public class PreviewTask
{
    public readonly NodeBase Node;

    public readonly Action Task;
    public readonly Action OnFinished;

    public readonly float CreatedAt;
    public float TimeSinceCreated => Time.time - CreatedAt;
    public readonly bool WasIdleBefore;

    public PreviewTask(NodeBase node, Action task, Action onFinished)
    {
        Node = node;
        Task = task;
        OnFinished = onFinished;
        CreatedAt = Time.time;
        WasIdleBefore = node.OngoingPreviewTask == null;
    }
}
