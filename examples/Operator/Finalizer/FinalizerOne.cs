using KubeOps.Abstractions.Finalizer;

using Operator.Entities;

namespace Operator.Finalizer;

public class FinalizerOne : IEntityFinalizer<V1TestEntity>
{
    public Task<V1TestEntity> FinalizeAsync(V1TestEntity entity, CancellationToken cancellationToken)
    {
        return Task.FromResult(entity);
    }
}
