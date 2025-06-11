using System.Reflection;

using k8s.Models;

using KubeOps.Abstractions.Rbac;

namespace KubeOps.Transpiler;

/// <summary>
/// Transpiler for Kubernetes RBAC attributes to create <see cref="V1PolicyRule"/>s.
/// </summary>
public static class Rbac
{
    /// <summary>
    /// Convert a list of <see cref="RbacAttribute"/>s to a list of <see cref="V1PolicyRule"/>s.
    /// The rules are grouped by entity type and verbs.
    /// </summary>
    /// <param name="attributes">List of <see cref="RbacAttribute"/>s.</param>
    /// <returns>A converted, grouped list of <see cref="V1PolicyRule"/>s.</returns>
    public static IEnumerable<V1PolicyRule> Transpile(
        this IEnumerable<RbacAttribute> attributes)
    {
        var list = attributes.ToList();

        var generic = list
            .OfType<GenericRbacAttribute>()
            .Select(a => new V1PolicyRule
            {
                ApiGroups = a.Groups,
                Resources = a.Resources,
                NonResourceURLs = a.Urls,
                Verbs = ConvertToStrings(a.Verbs),
            });

        var entities = list
            .OfType<EntityRbacAttribute>()
            .SelectMany(attribute =>
                attribute.Entities.Select(type => (EntityType: type, attribute.Verbs)))
            .GroupBy(e => e.EntityType)
            .Select(
                group => (
                    Crd: group.Key.ToEntityMetadata(),
                    Verbs: group.Aggregate(RbacVerb.None, (accumulator, element) => accumulator | element.Verbs)))
            .GroupBy(group => (group.Crd.Metadata.Group, group.Verbs))
            .Select(
                group => new V1PolicyRule
                {
                    ApiGroups = [group.Key.Group],
                    Resources = group.Select(crd => crd.Crd.Metadata.PluralName).Distinct().ToList(),
                    Verbs = ConvertToStrings(group.Key.Verbs),
                });

        var entityStatus = list
            .OfType<EntityRbacAttribute>()
            .SelectMany(attribute =>
                attribute.Entities.Select(type => (EntityType: type, attribute.Verbs)))
            .Where(e => e.EntityType.GetProperty("Status") != null)
            .GroupBy(e => e.EntityType)
            .Select(group => group.Key.ToEntityMetadata())
            .Select(
                crd => new V1PolicyRule
                {
                    ApiGroups = [crd.Metadata.Group],
                    Resources = [$"{crd.Metadata.PluralName}/status"],
                    Verbs = ConvertToStrings(RbacVerb.Get | RbacVerb.Patch | RbacVerb.Update),
                });

        return generic.Concat(entities).Concat(entityStatus);
    }

    private static string[] ConvertToStrings(RbacVerb verbs) => verbs switch
    {
        RbacVerb.None => Array.Empty<string>(),
        _ when verbs.HasFlag(RbacVerb.All) => ["*"],
        _ when verbs.HasFlag(RbacVerb.AllExplicit) =>
            Enum.GetValues<RbacVerb>()
                .Where(v => v != RbacVerb.All && v != RbacVerb.None && v != RbacVerb.AllExplicit)
                .Select(v => v.ToString().ToLowerInvariant())
                .ToArray(),
        _ =>
            Enum.GetValues<RbacVerb>()
                .Where(v => verbs.HasFlag(v) && v != RbacVerb.All && v != RbacVerb.None && v != RbacVerb.AllExplicit)
                .Select(v => v.ToString().ToLowerInvariant())
                .ToArray(),
    };
}
