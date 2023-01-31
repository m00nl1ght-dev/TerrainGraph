using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace TerrainGraph;

public abstract class AsyncPreviewScheduler : IPreviewScheduler
{
    private Thread _workerThread;
    private readonly Queue<PreviewTask> _queuedTasks = new();
    private EventWaitHandle _workEvent = new AutoResetEvent(false);
    private EventWaitHandle _disposeEvent = new AutoResetEvent(false);

    public void Init()
    {
        if (_workerThread != null) return;
        _workEvent = new AutoResetEvent(false);
        _disposeEvent = new AutoResetEvent(false);
        _workerThread = new Thread(DoThreadWork);
        _workerThread.Start();
    }

    public void Shutdown()
    {
        _queuedTasks.Clear();
        _disposeEvent?.Set();
        _workerThread = null;
        _workEvent = null;
        _disposeEvent = null;
    }

    public void ScheduleTask(PreviewTask task)
    {
        if (_workerThread == null)
        {
            BasicPreviewScheduler.Instance.ScheduleTask(task);
            return;
        }

        task.Node.OngoingPreviewTask = task;

        _queuedTasks.Enqueue(task);
        _workEvent.Set();
    }

    private void DoThreadWork()
    {
        var waitEvents = new WaitHandle[] { _workEvent, _disposeEvent };
        try
        {
            while (_queuedTasks.Count > 0 || WaitHandle.WaitAny(waitEvents) == 0)
            {
                Exception exception = null;
                if (_queuedTasks.Count > 0)
                {
                    var task = _queuedTasks.Dequeue();

                    if (task.Node.OngoingPreviewTask != task) continue;

                    try
                    {
                        task.Task.Invoke();
                    }
                    catch (Exception e)
                    {
                        exception = e;
                    }

                    RunOnMainThread(() =>
                    {
                        if (exception == null)
                        {
                            if (task.Node.TerrainCanvas.HasActiveGUI)
                            {
                                task.OnFinished?.Invoke();
                            }

                            if (task.Node.OngoingPreviewTask == task)
                            {
                                task.Node.OngoingPreviewTask = null;
                            }
                        }
                        else
                        {
                            OnError(task, exception);
                        }
                    });
                }
            }
        }
        catch (Exception e)
        {
            OnError(null, e);
        }
        finally
        {
            if (_workerThread == Thread.CurrentThread)
            {
                _workerThread = null;
                _workEvent = null;
                _disposeEvent = null;
            }

            foreach (var waitEvent in waitEvents)
            {
                waitEvent.Close();
            }
        }
    }

    protected abstract void RunOnMainThread(Action action);

    protected abstract void OnError(PreviewTask task, Exception exception);

    public abstract void DrawLoadingIndicator(NodeBase node, Rect rect);
}
