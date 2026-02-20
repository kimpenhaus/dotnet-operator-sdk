// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using KubeOps.Abstractions.Reconciliation;

namespace KubeOps.Operator.Queue;

/// <summary>
/// Represents an entry in the reconciliation queue for an entity of type <typeparamref name="TEntity"/>.
/// </summary>
/// <remarks>
/// <para>
/// Each entry contains an entity and the type of reconciliation operation (Added, Modified, or Deleted)
/// that determines how the entity will be processed by the reconciler.
/// </para>
/// <para>
/// The source of the reconciliation request can be either the Kubernetes API server
/// (via ResourceWatcher observing watch events) or an internal operation (error retry,
/// conflict retry, or periodic requeue).
/// </para>
/// </remarks>
/// <typeparam name="TEntity">
/// The type of the Kubernetes entity associated with this queue entry.
/// </typeparam>
public readonly record struct QueueEntry<TEntity>(TEntity Entity, ReconciliationType ReconciliationType, ReconciliationTriggerSource ReconciliationTriggerSource);
