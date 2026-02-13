// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Collections.Concurrent;
using System.Diagnostics;

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Builder;
using KubeOps.Abstractions.Reconciliation;
using KubeOps.KubernetesClient;
using KubeOps.Operator.Logging;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KubeOps.Operator.Queue;

/// <summary>
/// A background service responsible for managing the requeue mechanism of Kubernetes entities.
/// It processes entities from a timed queue and invokes the reconciliation logic for each entity.
/// </summary>
/// <typeparam name="TEntity">
/// The type of the Kubernetes entity being managed. This entity must implement the <see cref="IKubernetesObject{V1ObjectMeta}"/> interface.
/// </typeparam>
internal sealed class EntityRequeueBackgroundService<TEntity>(
    ActivitySource activitySource,
    IKubernetesClient client,
    OperatorSettings operatorSettings,
    ITimedEntityQueue<TEntity> queue,
    IReconciler<TEntity> reconciler,
    ILogger<EntityRequeueBackgroundService<TEntity>> logger) : IHostedService, IDisposable, IAsyncDisposable
    where TEntity : IKubernetesObject<V1ObjectMeta>
{
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _uidLocks = new();
    private readonly SemaphoreSlim _parallelismSemaphore = new(
        operatorSettings.ParallelReconciliationOptions.MaxParallelReconciliations,
        operatorSettings.ParallelReconciliationOptions.MaxParallelReconciliations);

    private bool _disposed;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // The current implementation of IHostedService expects that StartAsync is "really" asynchronous.
        // Blocking calls are not allowed, they would stop the rest of the startup flow.
        //
        // This is an open issue since 2019 and not expected to be closed soon. (https://github.com/dotnet/runtime/issues/36063)
        // For reasons unknown at the time of writing this code, "await Task.Yield()" didn't work as expected, it caused
        // a deadlock in 1/10 of the cases.
        //
        // Therefore, we use Task.Run() and put the work to queue. The passed cancellation token of the StartAsync
        // method is not used, because it would only cancel the scheduling (which we definitely don't want to cancel).
        // To make this intention explicit, CancellationToken.None gets passed.
        _ = Task.Run(() => WatchAsync(_cts.Token), CancellationToken.None);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
        => _disposed
            ? Task.CompletedTask
            : _cts.CancelAsync();

    public void Dispose()
    {
        _cts.Dispose();
        _parallelismSemaphore.Dispose();

        foreach (var lockItem in _uidLocks.Values)
        {
            lockItem.Dispose();
        }

        _uidLocks.Clear();
        client.Dispose();
        queue.Dispose();

        _disposed = true;
    }

    public async ValueTask DisposeAsync()
    {
        await CastAndDispose(_cts);
        await CastAndDispose(_parallelismSemaphore);

        foreach (var lockItem in _uidLocks.Values)
        {
            await CastAndDispose(lockItem);
        }

        _uidLocks.Clear();
        await CastAndDispose(client);
        await CastAndDispose(queue);

        _disposed = true;
        return;

        static async ValueTask CastAndDispose(IDisposable resource)
        {
            if (resource is IAsyncDisposable resourceAsyncDisposable)
            {
                await resourceAsyncDisposable.DisposeAsync();
            }
            else
            {
                resource.Dispose();
            }
        }
    }

    private async Task ReconcileSingleAsync(RequeueEntry<TEntity> entry, CancellationToken cancellationToken)
    {
        using var activity = activitySource.StartActivity($"""Processing requeued "{entry.RequeueType}" event""", ActivityKind.Consumer);
        using var scope = logger.BeginScope(EntityLoggingScope.CreateFor(entry.RequeueType, entry.Entity));

        logger.LogTrace("""Executing requested requeued reconciliation for "{Name}".""", entry.Entity.Name());

        if (await client.GetAsync<TEntity>(entry.Entity.Name(), entry.Entity.Namespace(), cancellationToken) is not
            { } entity)
        {
            logger.LogWarning(
                """Requeued entity "{Name}" was not found. Skipping reconciliation.""", entry.Entity.Name());
            return;
        }

        await reconciler.Reconcile(
            ReconciliationContext<TEntity>.CreateFromOperatorEvent(
                entity,
                entry.RequeueType.ToWatchEventType()),
            cancellationToken);
    }

    private async Task WatchAsync(CancellationToken cancellationToken)
    {
        var tasks = new List<Task>();

        await foreach (var queueEntry in queue.WithCancellation(cancellationToken))
        {
            var task = Task.Run(
                async () =>
                {
                    await _parallelismSemaphore.WaitAsync(cancellationToken);
                    try
                    {
                        await ProcessEntryAsync(queueEntry, cancellationToken);
                    }
                    finally
                    {
                        _parallelismSemaphore.Release();
                    }
                },
                cancellationToken);

            tasks.Add(task);
            tasks.RemoveAll(t => t.IsCompleted);
        }

        await Task.WhenAll(tasks);
    }

    private async Task ProcessEntryAsync(RequeueEntry<TEntity> entry, CancellationToken cancellationToken)
    {
        var uid = entry.Entity.Uid();

        var uidLock = _uidLocks.GetOrAdd(uid, _ => new(1, 1));

        try
        {
            var canAcquireLock = operatorSettings.ParallelReconciliationOptions.ConflictStrategy switch
            {
                ParallelReconciliationConflictStrategy.Discard => await uidLock.WaitAsync(0, cancellationToken),
                ParallelReconciliationConflictStrategy.RequeueAfterDelay => await uidLock.WaitAsync(0, cancellationToken),
                ParallelReconciliationConflictStrategy.WaitForCompletion => true,
                _ => throw new NotSupportedException($"Conflict strategy {operatorSettings.ParallelReconciliationOptions.ConflictStrategy} is not supported."),
            };

            if (!canAcquireLock)
            {
                await HandleLockingConflictAsync(entry, uid, cancellationToken);
                return;
            }

            if (operatorSettings.ParallelReconciliationOptions.ConflictStrategy is ParallelReconciliationConflictStrategy.WaitForCompletion)
            {
                await uidLock.WaitAsync(cancellationToken);
            }

            try
            {
                logger.LogDebug(
                    "Starting reconciliation for {Kind}/{Name} (UID: {Uid}).",
                    entry.Entity.Kind,
                    entry.Entity.Name(),
                    uid);

                await ReconcileSingleAsync(entry, cancellationToken);

                logger.LogDebug(
                    "Completed reconciliation for {Kind}/{Name} (UID: {Uid}).",
                    entry.Entity.Kind,
                    entry.Entity.Name(),
                    uid);
            }
            finally
            {
                uidLock.Release();
            }
        }
        catch (OperationCanceledException e) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(
                e,
                """Queued reconciliation for the entity of type {ResourceType} for "{Kind}/{Name}" (UID: {Uid}) failed.""",
                typeof(TEntity).Name,
                entry.Entity.Kind,
                entry.Entity.Name(),
                uid);
        }
        catch (Exception e)
        {
            logger.LogError(
                e,
                """Queued reconciliation for the entity of type {ResourceType} for "{Kind}/{Name}" (UID: {Uid}) failed.""",
                typeof(TEntity).Name,
                entry.Entity.Kind,
                entry.Entity.Name(),
                uid);
        }
        finally
        {
            if (uidLock.CurrentCount is 1 && _uidLocks.TryRemove(uid, out var removedLock))
            {
                removedLock.Dispose();
            }
        }
    }

    private async Task HandleLockingConflictAsync(RequeueEntry<TEntity> entry, string uid, CancellationToken cancellationToken)
    {
        switch (operatorSettings.ParallelReconciliationOptions.ConflictStrategy)
        {
            case ParallelReconciliationConflictStrategy.Discard:
                logger.LogDebug(
                    "Entity {Kind}/{Name} (UID: {Uid}) is already being reconciled. Discarding request.",
                    entry.Entity.Kind,
                    entry.Entity.Name(),
                    uid);
                break;

            case ParallelReconciliationConflictStrategy.RequeueAfterDelay:
                logger.LogDebug(
                    "Entity {Kind}/{Name} (UID: {Uid}) is already being reconciled. Requeueing after {Delay}s.",
                    entry.Entity.Kind,
                    entry.Entity.Name(),
                    uid,
                    operatorSettings.ParallelReconciliationOptions.GetEffectiveRequeueDelay().TotalSeconds);

                await queue.Enqueue(
                    entry.Entity,
                    entry.RequeueType,
                    operatorSettings.ParallelReconciliationOptions.GetEffectiveRequeueDelay(),
                    cancellationToken);
                break;

            default:
                throw new NotSupportedException($"Conflict strategy {operatorSettings.ParallelReconciliationOptions.ConflictStrategy} is not supported in HandleUidConflictAsync.");
        }
    }
}
