// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Threading;

namespace NzbDrone.Core.SystemServices;

/// <summary>
/// Marshals SetThreadExecutionState onto one dedicated thread. ES_CONTINUOUS state is per-thread on Windows.
/// </summary>
internal sealed class WindowsSleepInhibitionWorker : IDisposable
{
    private readonly Func<uint, uint> setExecutionState;
    private readonly BlockingCollection<Action> queue = new();
    private readonly Thread thread;
    private int disposed;
    private bool isInhibited;

    internal WindowsSleepInhibitionWorker(Func<uint, uint> setExecutionState)
    {
        this.setExecutionState = setExecutionState ?? throw new ArgumentNullException(nameof(setExecutionState));
        this.thread = new Thread(this.Run)
        {
            IsBackground = true,
            Name = "Leecharr Windows sleep inhibition",
        };
        this.thread.Start();
    }

    internal int WorkerThreadId => this.thread.ManagedThreadId;

    internal bool Apply(bool inhibit)
    {
        if (Volatile.Read(ref this.disposed) != 0)
        {
            return false;
        }

        var completion = new ManualResetEventSlim(false);
        var success = false;
        this.queue.Add(() =>
        {
            try
            {
                success = this.ApplyOnWorkerThread(inhibit);
            }
            finally
            {
                completion.Set();
            }
        });
        completion.Wait();
        completion.Dispose();
        return success;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref this.disposed, 1) != 0)
        {
            return;
        }

        this.queue.CompleteAdding();
        this.thread.Join(TimeSpan.FromSeconds(5));
        this.queue.Dispose();
    }

    private void Run()
    {
        try
        {
            foreach (var work in this.queue.GetConsumingEnumerable())
            {
                work();
            }
        }
        finally
        {
            if (this.isInhibited)
            {
                this.setExecutionState((uint)PowerManagementService.ExecutionState.EsContinuous);
                this.isInhibited = false;
            }
        }
    }

    private bool ApplyOnWorkerThread(bool inhibit)
    {
        if (inhibit)
        {
            var flags = (uint)(PowerManagementService.ExecutionState.EsContinuous | PowerManagementService.ExecutionState.EsSystemRequired | PowerManagementService.ExecutionState.EsAwaymodeRequired);
            var result = this.setExecutionState(flags);
            if (result == 0)
            {
                flags = (uint)(PowerManagementService.ExecutionState.EsContinuous | PowerManagementService.ExecutionState.EsSystemRequired);
                result = this.setExecutionState(flags);
            }

            this.isInhibited = result != 0;
            return this.isInhibited;
        }

        var resetResult = this.setExecutionState((uint)PowerManagementService.ExecutionState.EsContinuous);
        this.isInhibited = false;
        return resetResult != 0;
    }
}
