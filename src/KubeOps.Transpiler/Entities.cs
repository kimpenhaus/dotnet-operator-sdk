using System.Reflection;

using k8s.Models;

using KubeOps.Abstractions.Entities;
using KubeOps.Abstractions.Entities.Attributes;

namespace KubeOps.Transpiler;

/// <summary>
/// Transpiler for Kubernetes entities to create entity metadata.
/// </summary>
public static class Entities
{
    private const string Namespaced = "Namespaced";

    /// <summary>
    /// Create a metadata / scope tuple out of a given entity type via reflection in the same loaded assembly.
    /// </summary>
    /// <param name="entityType">The type to convert.</param>
    /// <returns>A tuple that contains <see cref="EntityMetadata"/> and a scope.</returns>
    /// <exception cref="ArgumentException">Thrown when the type contains no <see cref="KubernetesEntityAttribute"/>.</exception>
    public static (EntityMetadata Metadata, string Scope) ToEntityMetadata(this Type entityType)
        => (entityType.GetCustomAttribute<KubernetesEntityAttribute>(),
                entityType.GetCustomAttribute<EntityScopeAttribute>()) switch
        {
            (null, _) => throw new ArgumentException("The given type is not a valid Kubernetes entity."),
            ({ } attr, var scope) => (new(
                    Defaulted(attr.Kind, entityType.Name),
                    Defaulted(attr.ApiVersion, "v1"),
                    attr.Group,
                    attr.PluralName),
                scope switch
                {
                    null => Enum.GetName(EntityScope.Namespaced) ?? Namespaced,
                    _ => Enum.GetName(scope.Scope) ?? Namespaced,
                }),
        };

    /// <summary>
    /// Create a metadata / scope tuple out of a given entity type via reflection in the same loaded assembly.
    /// </summary>
    /// <typeparam name="TEntity">The type to convert.</typeparam>
    /// <returns>A tuple that contains <see cref="EntityMetadata"/> and a scope.</returns>
    /// <exception cref="ArgumentException">Thrown when the type contains no <see cref="KubernetesEntityAttribute"/>.</exception>
    public static (EntityMetadata Metadata, string Scope) ToEntityMetadata<TEntity>()
        => (typeof(TEntity).GetCustomAttribute<KubernetesEntityAttribute>(),
                typeof(TEntity).GetCustomAttribute<EntityScopeAttribute>()) switch
        {
            (null, _) => throw new ArgumentException("The given type is not a valid Kubernetes entity."),
            ({ } attr, var scope) => (new(
                    Defaulted(attr.Kind, typeof(TEntity).Name),
                    Defaulted(attr.ApiVersion, "v1"),
                    attr.Group,
                    attr.PluralName),
                scope switch
                {
                    null => Enum.GetName(EntityScope.Namespaced) ?? Namespaced,
                    _ => Enum.GetName(scope.Scope) ?? Namespaced,
                }),
        };

    private static string Defaulted(string? value, string defaultValue) =>
        string.IsNullOrWhiteSpace(value) ? defaultValue : value;
}
