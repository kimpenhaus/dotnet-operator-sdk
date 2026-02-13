// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Abstractions.Builder;

/// <summary>
/// Provides configuration options for parallel reconciliation processing of Kubernetes entities.
/// </summary>
/// <remarks>
/// <para>
/// This configuration controls how the operator handles concurrent reconciliation requests
/// for multiple entities. The settings balance between throughput (maximum parallelism) and
/// consistency (UID-based locking to prevent concurrent reconciliation of the same entity).
/// </para>
/// <para>
/// When an entity is being reconciled, subsequent reconciliation requests for the same UID
/// (Unique Identifier) are handled according to the configured <see cref="ConflictStrategy"/>.
/// This prevents race conditions and ensures data consistency during entity processing.
/// </para>
/// </remarks>
/// <example>
/// <code language="csharp">
/// // Example 1: Using default WaitForCompletion strategy
/// var options1 = new ParallelReconciliationOptions
/// {
///     MaxParallelReconciliations = 10
///     // ConflictStrategy defaults to WaitForCompletion
/// };
///
/// // Example 2: Using Discard strategy for higher throughput
/// var options2 = new ParallelReconciliationOptions
/// {
///     MaxParallelReconciliations = 10,
///     ConflictStrategy = ParallelReconciliationConflictStrategy.Discard
/// };
///
/// // Example 3: Using RequeueAfterDelay strategy with custom delay
/// var options3 = new ParallelReconciliationOptions
/// {
///     MaxParallelReconciliations = 10,
///     ConflictStrategy = ParallelReconciliationConflictStrategy.RequeueAfterDelay,
///     RequeueDelay = TimeSpan.FromSeconds(3) // Optional, defaults to 5 seconds
/// };
/// </code>
/// </example>
public sealed record ParallelReconciliationOptions
{
    private int _maxParallelReconciliations = Environment.ProcessorCount * 2;

    /// <summary>
    /// Gets or sets the maximum number of parallel reconciliations across all entities.
    /// </summary>
    /// <value>
    /// The maximum number of entities that can be reconciled concurrently.
    /// The default is twice the number of processor cores (<see cref="Environment.ProcessorCount"/> * 2).
    /// </value>
    /// <remarks>
    /// <para>
    /// This setting limits the total number of concurrent reconciliation operations to prevent
    /// resource exhaustion. A higher value increases throughput but consumes more CPU and memory.
    /// A lower value reduces resource usage but may increase latency for entity reconciliation.
    /// </para>
    /// <para>
    /// The default value is based on the assumption that reconciliation operations are I/O-bound
    /// (e.g., making API calls to Kubernetes), which typically allows for higher parallelism
    /// than CPU-bound operations.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The value is less than or equal to 0.
    /// </exception>
    public int MaxParallelReconciliations
    {
        get => _maxParallelReconciliations;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, 0);
            _maxParallelReconciliations = value;
        }
    }

    /// <summary>
    /// Gets or sets the strategy for handling reconciliation requests when an entity with the same UID is already being processed.
    /// </summary>
    /// <value>
    /// One of the enumeration values that specifies how to handle concurrent reconciliation attempts for the same entity.
    /// The default is <see cref="ParallelReconciliationConflictStrategy.WaitForCompletion"/>.
    /// </value>
    /// <remarks>
    /// <para>
    /// Each Kubernetes entity has a unique identifier (UID). When the operator receives multiple reconciliation
    /// requests for the same UID while a reconciliation is already in progress, this strategy determines the behavior:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// <see cref="ParallelReconciliationConflictStrategy.WaitForCompletion"/>: Blocks until the current reconciliation
    /// completes, then processes the request sequentially. This ensures no reconciliation requests are lost.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <see cref="ParallelReconciliationConflictStrategy.Discard"/>: Ignores the new request, assuming the current
    /// reconciliation will handle the latest state. This is the most performant option.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <see cref="ParallelReconciliationConflictStrategy.RequeueAfterDelay"/>: Requeues the request to be processed
    /// after the configured <see cref="RequeueDelay"/>, ensuring no updates are lost while avoiding immediate blocking.
    /// </description>
    /// </item>
    /// </list>
    /// </remarks>
    /// <seealso cref="ParallelReconciliationConflictStrategy"/>
    public ParallelReconciliationConflictStrategy ConflictStrategy { get; set; } = ParallelReconciliationConflictStrategy.WaitForCompletion;

    /// <summary>
    /// Gets or sets the delay before requeueing an entity when <see cref="ConflictStrategy"/> is set to <see cref="ParallelReconciliationConflictStrategy.RequeueAfterDelay"/>.
    /// </summary>
    /// <value>
    /// The time span to wait before requeueing a conflicting reconciliation request, or <see langword="null"/> to use the default delay.
    /// When <see langword="null"/> and <see cref="ConflictStrategy"/> is <see cref="ParallelReconciliationConflictStrategy.RequeueAfterDelay"/>,
    /// a default delay of 5 seconds is used. The default is <see langword="null"/>.
    /// </value>
    /// <remarks>
    /// <para>
    /// This property is only used when <see cref="ConflictStrategy"/> is set to
    /// <see cref="ParallelReconciliationConflictStrategy.RequeueAfterDelay"/>. When using other strategies,
    /// this value is ignored.
    /// </para>
    /// <para>
    /// The delay provides a grace period for the current reconciliation to complete, reducing the likelihood of
    /// immediate re-conflicts. A longer delay reduces system load but increases latency for
    /// processing entity updates. A shorter delay provides faster response but may result in
    /// more frequent requeueing if reconciliations take longer than the delay.
    /// </para>
    /// </remarks>
    public TimeSpan? RequeueDelay { get; set; }

    /// <summary>
    /// Gets the effective requeue delay, using a default value if <see cref="RequeueDelay"/> is <see langword="null"/>.
    /// </summary>
    /// <returns>
    /// The configured <see cref="RequeueDelay"/> if set; otherwise, a default of 5 seconds.
    /// </returns>
    /// <remarks>
    /// This method is useful when implementing the requeueing logic, as it provides a sensible
    /// default value when the delay is not explicitly configured.
    /// </remarks>
    public TimeSpan GetEffectiveRequeueDelay() => RequeueDelay ?? TimeSpan.FromSeconds(5);
}
