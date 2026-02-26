# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Build the solution
dotnet build

# Build in Release mode (warnings are treated as errors)
dotnet build --configuration Release

# Run all tests (requires a running Kubernetes cluster)
dotnet test --configuration Release

# Run a single test project
dotnet test test/KubeOps.Operator.Test/KubeOps.Operator.Test.csproj

# Run a single test by name
dotnet test --filter "FullyQualifiedName~TestMethodName"

# Check formatting (must pass before tests in CI)
dotnet format --verify-no-changes

# Fix formatting
dotnet format
```

Integration tests require a live Kubernetes cluster (kind is used in CI via `helm/kind-action`). Unit tests do not.

## Architecture

KubeOps is a modular Kubernetes Operator SDK for .NET. The solution is split into independent NuGet packages under `src/` with corresponding test projects in `test/`.

### Package Dependency Order

```
KubeOps.Abstractions        ← core interfaces, entities, attributes, settings
KubeOps.Transpiler          ← converts .NET types to Kubernetes YAML (CRDs, RBAC)
KubeOps.KubernetesClient    ← enhanced client built on k8s.IKubernetes
KubeOps.Generator           ← Roslyn source generator (build-time only)
KubeOps.Operator            ← runtime engine: watcher → queue → reconciler
KubeOps.Operator.Web        ← ASP.NET Core integration for webhooks
KubeOps.Cli                 ← dotnet tool for scaffolding and CRD generation
KubeOps.Templates           ← dotnet new project templates
```

### Reconciliation Pipeline (core data flow)

The operator runtime in `KubeOps.Operator` uses a three-stage pipeline per entity type `TEntity`:

1. **`ResourceWatcher<TEntity>`** (`Watcher/`) — an `IHostedService` that opens a Kubernetes watch stream via `IKubernetesClient.WatchAsync`. It uses a FusionCache to track the last observed `Generation` per entity UID and **only enqueues events where the generation actually changed** (or for Delete events). Avoids reconciling status-only updates.

2. **`TimedEntityQueue<TEntity>`** (`Queue/`) — an in-memory async queue (`ITimedEntityQueue<TEntity>`) that supports delayed/scheduled entries. The watcher enqueues with `TimeSpan.Zero`; the reconciler can request requeue with a delay.

3. **`EntityQueueBackgroundService<TEntity>`** (`Queue/`) — an `IHostedService` that reads from the queue and calls `Reconciler<TEntity>.Reconcile()`. Implements a two-level locking strategy:
   - A global `_parallelismSemaphore` limits total concurrent reconciliations (back-pressure before reading from queue)
   - Per-UID `SemaphoreSlim` locks prevent concurrent reconciliation of the same entity
   - Conflict strategies: `Discard`, `RequeueAfterDelay`, or `WaitForCompletion` (default)

4. **`Reconciler<TEntity>`** (`Reconciliation/`) — resolves the fresh entity from the API server, then dispatches:
   - `ReconciliationType.Added/Modified` + no deletion timestamp → `IEntityController<TEntity>.ReconcileAsync`
   - `ReconciliationType.Added/Modified` + deletion timestamp + finalizers → `IEntityFinalizer<TEntity>.FinalizeAsync` (sequential, one at a time)
   - `ReconciliationType.Deleted` → `IEntityController<TEntity>.DeletedAsync`
   - Auto-attaches/detaches finalizers if `OperatorSettings.AutoAttachFinalizers/AutoDetachFinalizers = true`

### Key Abstractions (`KubeOps.Abstractions`)

- **`CustomKubernetesEntity`** / **`CustomKubernetesEntity<TSpec>`** / **`CustomKubernetesEntity<TSpec,TStatus>`** — base classes for defining CRDs
- **`IEntityController<TEntity>`** — implement `ReconcileAsync` and `DeletedAsync`
- **`IEntityFinalizer<TEntity>`** — implement `FinalizeAsync` for cleanup logic
- **`ReconciliationResult<TEntity>`** — return type from controller/finalizer methods; includes `IsSuccess`, `Entity`, and optional `RequeueAfter`
- **`OperatorSettings`** — controls namespace, leader election, queue strategy, parallelism, finalizer auto-attach behavior

### Registration via `IOperatorBuilder`

`services.AddKubernetesOperator()` returns an `IOperatorBuilder`. Each `AddController<TImpl, TEntity>()` call registers the entire watcher→queue→reconciler pipeline for that entity type. `KubeOps.Generator` (Roslyn source generator) generates `RegisterComponents()` which calls all `AddController`, `AddFinalizer`, and `AddWebhook` registrations automatically.

### Webhooks (`KubeOps.Operator.Web`)

Requires ASP.NET Core. Provides admission webhooks (validating and mutating) and conversion webhooks. Registered via `IOperatorBuilder` extensions from the Web package.

### Leader Election

When `OperatorSettings.LeaderElectionType = LeaderElectionType.Single`, `LeaderAwareResourceWatcher<TEntity>` is used instead of `ResourceWatcher<TEntity>`. The watcher only starts processing events when this instance holds the leader lease.

## Code Conventions

- All source files must start with the Apache 2.0 license header (`IDE0073` enforced as warning)
- C# 12, `ImplicitUsings`, `Nullable` enabled everywhere
- Source packages target `net8.0`, `net9.0` and `net10.0`; test projects target `net10.0`
- StyleCop, SonarAnalyzer, and Roslynator analyzers are active on all source projects
- `InternalsVisibleTo` is set automatically for each `ProjectName.Test` project and `DynamicProxyGenAssembly2` (Moq)
- Warnings are errors in Release builds — always check `dotnet build --configuration Release`
- PR names must follow [Conventional Commits](https://www.conventionalcommits.org/)
- Max line length: 120 characters
- testing frameworks: xUnit, FluentAssertions, Moq
- Use expression-bodied members where possible, especially for simple one-liners
- Use `var` when the type is obvious from the right-hand side; otherwise, use
- Use target-typed `new()` when the type can be inferred from the left-hand side