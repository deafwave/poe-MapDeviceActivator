using System.Collections.Generic;
using System.Windows.Forms;
using ExileCore;
using ExileCore.Shared;

namespace MapDeviceActivator;

public class Scheduler
{
    public Queue<SyncTask<bool>> Tasks = new();
    public SyncTask<bool> CurrentTask = null;

    public void StopAllRoutines()
    {
        Stop();
        Clear();
        InputAsync.LOCK_CONTROLLER = false;
        InputAsync.IControllerEnd();
        Input.KeyUp(Keys.ControlKey);
    }

    public Scheduler(params SyncTask<bool>[] tasks)
    {
        foreach (var task in tasks)
        {
            Tasks.Enqueue(task);
        }
    }

    public void AddTask(SyncTask<bool> task, string name = null)
    {
        Tasks.Enqueue(task);
    }

    public void Run()
    {
        if (CurrentTask == null && Tasks.Count > 0)
        {
            CurrentTask = Tasks.Dequeue();
            CurrentTask.GetAwaiter().OnCompleted(() =>
            {
                CurrentTask = null;
            });
        }
        if (CurrentTask != null)
        {
            InputAsync.LOCK_CONTROLLER = true;
            TaskUtils.RunOrRestart(ref CurrentTask, () => null);
        }
    }

    public void Stop()
    {
        CurrentTask = null;
    }

    public void Clear()
    {
        Tasks.Clear();
    }
}
