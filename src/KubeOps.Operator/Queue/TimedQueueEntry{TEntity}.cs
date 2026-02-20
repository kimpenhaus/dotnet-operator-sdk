// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Collections.Concurrent;

using KubeOps.Abstractions.Reconciliation;

namespace KubeOps.Operator.Queue;

internal sealed record TimedQueueEntry<TEntity> : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly TimeSpan _queueIn;
    private readonly TEntity _entity;
    private readonly ReconciliationType _reconciliationType;
    private readonly ReconciliationTriggerSource _reconciliationTriggerSource;

    public TimedQueueEntry(TEntity entity, ReconciliationType reconciliationType, ReconciliationTriggerSource reconciliationTriggerSource, TimeSpan queueIn)
    {
        _queueIn = queueIn;
        _entity = entity;
        _reconciliationType = reconciliationType;
        _reconciliationTriggerSource = reconciliationTriggerSource;
    }

    /// <summary>
    /// A <see cref="CancellationToken"/> that is triggered after calling <see cref="Cancel"/>.
    /// </summary>
    public CancellationToken Token => _cts.Token;

    public void Dispose() => _cts.Dispose();

    /// <summary>
    /// Cancels the execution of <see cref="AddAfterDelay"/> and disposes any associated resources.
    /// </summary>
    public void Cancel()
    {
        _cts.Cancel();
        Dispose();
    }

    public async Task AddAfterDelay(BlockingCollection<QueueEntry<TEntity>> collection)
    {
        try
        {
            await Task.Delay(_queueIn, _cts.Token);
            if (_cts.IsCancellationRequested)
            {
                return;
            }

            collection.TryAdd(new(_entity, _reconciliationType, _reconciliationTriggerSource));
        }
        catch (TaskCanceledException)
        {
            // Ignore canceled tasks
        }
        catch (ObjectDisposedException)
        {
            // And also if the object is disposed.
        }
    }
}
