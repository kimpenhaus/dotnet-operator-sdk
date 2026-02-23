// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using KubeOps.Abstractions.Reconciliation;

namespace KubeOps.Operator.Queue;

/// <summary>
/// Represents a scheduled queue entry that will be added to the reconciliation queue
/// at a specific time in the future.
/// </summary>
/// <typeparam name="TEntity">The type of the Kubernetes entity.</typeparam>
/// <remarks>
/// This implementation uses a timer-based approach instead of Task.Delay to reduce
/// memory overhead when many entities are scheduled with long delays.
/// </remarks>
internal sealed record TimedQueueEntry<TEntity>
{
    private readonly TEntity _entity;
    private readonly ReconciliationType _reconciliationType;
    private readonly ReconciliationTriggerSource _reconciliationTriggerSource;

    /// <summary>
    /// Initializes a new instance of the <see cref="TimedQueueEntry{TEntity}"/> class.
    /// </summary>
    /// <param name="entity">The entity to be reconciled.</param>
    /// <param name="reconciliationType">The type of reconciliation operation.</param>
    /// <param name="reconciliationTriggerSource">The source of the reconciliation request.</param>
    /// <param name="queueIn">The delay before the entry should be added to the queue.</param>
    public TimedQueueEntry(TEntity entity, ReconciliationType reconciliationType, ReconciliationTriggerSource reconciliationTriggerSource, TimeSpan queueIn)
    {
        _entity = entity;
        _reconciliationType = reconciliationType;
        _reconciliationTriggerSource = reconciliationTriggerSource;
        EnqueueAt = DateTimeOffset.UtcNow.Add(queueIn);
    }

    /// <summary>
    /// Gets the timestamp when this entry should be added to the queue.
    /// </summary>
    public DateTimeOffset EnqueueAt { get; }

    /// <summary>
    /// Gets a value indicating whether this entry has been cancelled.
    /// </summary>
    public bool IsCancelled { get; private set; }

    /// <summary>
    /// Marks this entry as cancelled, preventing it from being added to the queue.
    /// </summary>
    public void Cancel()
    {
        IsCancelled = true;
    }

    /// <summary>
    /// Creates a <see cref="QueueEntry{TEntity}"/> from this timed entry.
    /// </summary>
    /// <returns>A queue entry ready for reconciliation processing.</returns>
    public QueueEntry<TEntity> ToQueueEntry()
        => new(_entity, _reconciliationType, _reconciliationTriggerSource);
}
