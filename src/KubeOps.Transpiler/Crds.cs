using System.Collections;
using System.Reflection;
using System.Text.Json.Serialization;

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Entities;
using KubeOps.Abstractions.Entities.Attributes;
using KubeOps.Transpiler.Kubernetes;

namespace KubeOps.Transpiler;

/// <summary>
/// CRD transpiler for Kubernetes entities.
/// </summary>
public static class Crds
{
    private const string Integer = "integer";
    private const string Number = "number";
    private const string String = "string";
    private const string Boolean = "boolean";
    private const string Object = "object";
    private const string Array = "array";

    private const string Int32 = "int32";
    private const string Int64 = "int64";
    private const string Float = "float";
    private const string Double = "double";
    private const string Decimal = "decimal";
    private const string DateTime = "date-time";

    private static readonly string[] IgnoredToplevelProperties = ["metadata", "apiversion", "kind"];

    /// <summary>
    /// Transpile a single type to a CRD.
    /// </summary>
    /// <param name="type">The type to convert.</param>
    /// <returns>The converted custom resource definition.</returns>
    public static V1CustomResourceDefinition Transpile(this Type type)
    {
        var (meta, scope) = type.ToEntityMetadata();
        var crd = new V1CustomResourceDefinition(new()).Initialize();

        crd.Metadata.Name = $"{meta.PluralName}.{meta.Group}";
        crd.Spec.Group = meta.Group;

        crd.Spec.Names =
            new V1CustomResourceDefinitionNames
            {
                Kind = meta.Kind,
                ListKind = meta.ListKind,
                Singular = meta.SingularName,
                Plural = meta.PluralName,
            };
        crd.Spec.Scope = scope;
        if (type.GetCustomAttribute<KubernetesEntityShortNamesAttribute>()?.ShortNames is { } shortNames)
        {
            crd.Spec.Names.ShortNames = shortNames;
        }

        var version = new V1CustomResourceDefinitionVersion(meta.Version, true, true);
        if
            (type.GetProperty("Status") != null
             || type.GetProperty("status") != null)
        {
            version.Subresources = new V1CustomResourceSubresources(null, new object());
        }

        version.Schema = new V1CustomResourceValidation(new V1JSONSchemaProps
        {
            Type = Object,
            Description = type.GetCustomAttribute<DescriptionAttribute>()?.Description,
            Properties = type.GetProperties()
                .Where(p => !IgnoredToplevelProperties.Contains(p.Name.ToLowerInvariant())
                            && p.GetCustomAttribute<IgnoreAttribute>() == null)
                .Select(p => (Name: p.GetPropertyName(), Schema: p.Map()))
                .ToDictionary(t => t.Name, t => t.Schema),
        });

        version.AdditionalPrinterColumns = type.MapPrinterColumns().ToList() switch
        {
            { Count: > 0 } l => l,
            _ => null,
        };
        crd.Spec.Versions = new List<V1CustomResourceDefinitionVersion> { version };
        crd.Validate();

        return crd;
    }

    /// <summary>
    /// Transpile a list of entities to CRDs and group them by version.
    /// </summary>
    /// <param name="types">The types to convert.</param>
    /// <returns>The converted custom resource definitions.</returns>
    public static IEnumerable<V1CustomResourceDefinition> Transpile(
        this IEnumerable<Type> types)
        => types
            .Where(type => type.Assembly != typeof(KubernetesEntityAttribute).Assembly
                           && type.GetCustomAttributes<KubernetesEntityAttribute>().Any()
                           && !type.GetCustomAttributes<IgnoreAttribute>().Any())
            .Select(type => (Props: type.Transpile(),
                IsStorage: type.GetCustomAttributes<StorageVersionAttribute>().Any()))
            .GroupBy(grp => grp.Props.Metadata.Name)
            .Select(
                group =>
                {
                    if (group.Count(def => def.IsStorage) > 1)
                    {
                        throw new ArgumentException("There are multiple stored versions on an entity.");
                    }

                    var crd = group.First().Props;
                    crd.Spec.Versions = group
                        .SelectMany(
                            c => c.Props.Spec.Versions.Select(
                                v =>
                                {
                                    v.Served = true;
                                    v.Storage = c.IsStorage;
                                    return v;
                                }))
                        .OrderByDescending(v => v.Name, new KubernetesVersionComparer())
                        .ToList();

                    // when only one version exists, or when no StorageVersion attributes are found
                    // the first version in the list is the stored one.
                    if (crd.Spec.Versions.Count == 1 || !group.Any(def => def.IsStorage))
                    {
                        crd.Spec.Versions[0].Storage = true;
                    }

                    return crd;
                });

    private static string GetPropertyName(this PropertyInfo prop)
    {
        var name = prop.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? prop.Name;

        return $"{name[..1].ToLowerInvariant()}{name[1..]}";
    }

    private static IEnumerable<V1CustomResourceColumnDefinition> MapPrinterColumns(this Type type)
    {
        var props = type.GetProperties().Select(p => (Prop: p, Path: string.Empty)).ToList();
        while (props.Count > 0)
        {
            var (prop, path) = props[0];
            props.RemoveAt(0);

            if (prop.PropertyType.IsClass)
            {
                props.AddRange(prop.PropertyType.GetProperties()
                    .Select(p => (Prop: p, Path: $"{path}.{prop.GetPropertyName()}")));
            }

            if (prop.GetCustomAttribute<AdditionalPrinterColumnAttribute>() is not { } attr)
            {
                continue;
            }

            var mapped = prop.Map();
            yield return new V1CustomResourceColumnDefinition
            {
                Name = attr.Name ?? prop.GetPropertyName(),
                JsonPath = $"{path}.{prop.GetPropertyName()}",
                Type = mapped.Type,
                Description = mapped.Description,
                Format = mapped.Format,
                Priority = attr.Priority switch
                {
                    PrinterColumnPriority.StandardView => 0,
                    _ => 1,
                },
            };
        }

        foreach (var attr in type.GetCustomAttributes<GenericAdditionalPrinterColumnAttribute>())
        {
            yield return new V1CustomResourceColumnDefinition
            {
                Name = attr.Name,
                JsonPath = attr.JsonPath,
                Type = attr.Type,
                Description = attr.Description,
                Format = attr.Format,
                Priority = attr.Priority switch
                {
                    PrinterColumnPriority.StandardView => 0,
                    _ => 1,
                },
            };
        }
    }

    private static V1JSONSchemaProps Map(this PropertyInfo prop)
    {
        var props = prop.PropertyType.Map();

        props.Description ??= prop.GetCustomAttribute<DescriptionAttribute>()?.Description;

        if (prop.IsNullable())
        {
            // Default to Nullable to null to avoid generating `nullable:false`
            props.Nullable = true;
        }

        if (prop.GetCustomAttribute<ExternalDocsAttribute>() is { } extDocs)
        {
            props.ExternalDocs = new V1ExternalDocumentation(
                extDocs.Description,
                extDocs.Url);
        }

        if (prop.GetCustomAttribute<ItemsAttribute>() is { } items)
        {
            props.MinItems = items.MinItems;
            props.MaxItems = items.MaxItems;
        }

        if (prop.GetCustomAttribute<LengthAttribute>() is { } length)
        {
            props.MinLength = length.MinLength;
            props.MaxLength = length.MaxLength;
        }

        if (prop.GetCustomAttribute<MultipleOfAttribute>() is { } multi)
        {
            props.MultipleOf = multi.Value;
        }

        if (prop.GetCustomAttribute<PatternAttribute>() is { } pattern)
        {
            props.Pattern = pattern.RegexPattern;
        }

        if (prop.GetCustomAttribute<RangeMaximumAttribute>() is { } rangeMax)
        {
            props.Maximum = rangeMax.Maximum;
            props.ExclusiveMaximum = rangeMax.ExclusiveMaximum;
        }

        if (prop.GetCustomAttribute<RangeMinimumAttribute>() is { } rangeMin)
        {
            props.Minimum = rangeMin.Minimum;
            props.ExclusiveMinimum = rangeMin.ExclusiveMinimum;
        }

        if (prop.GetCustomAttribute<PreserveUnknownFieldsAttribute>() is not null)
        {
            props.XKubernetesPreserveUnknownFields = true;
        }

        if (prop.GetCustomAttribute<EmbeddedResourceAttribute>() is not null)
        {
            props.XKubernetesEmbeddedResource = true;
            props.XKubernetesPreserveUnknownFields = true;
            props.Type = Object;
            props.Properties = null;
        }

        if (prop.GetCustomAttributes<ValidationRuleAttribute>().ToArray() is { Length: > 0 } validations)
        {
            props.XKubernetesValidations = validations
                .Select(validation => new V1ValidationRule(
                    validation.Rule,
                    fieldPath: validation.FieldPath,
                    message: validation.Message,
                    messageExpression: validation.MessageExpression,
                    reason: validation.Reason))
                .ToList();
        }

        return props;
    }

    private static V1JSONSchemaProps Map(this Type type)
    {
        if (type.FullName == "System.String")
        {
            return new V1JSONSchemaProps { Type = String };
        }

        if (type.FullName == "System.Object")
        {
            return new V1JSONSchemaProps { Type = Object, XKubernetesPreserveUnknownFields = true };
        }

        if (type.Name == typeof(Nullable<>).Name && type.GenericTypeArguments.Length == 1)
        {
            var props = type.GenericTypeArguments[0].Map();
            props.Nullable = true;
            return props;
        }

        var interfaces = (type.IsInterface
            ? type.GetInterfaces().Append(type)
            : type.GetInterfaces()).ToList();

        var interfaceNames = interfaces.Select(i =>
            i.IsGenericType
                ? i.GetGenericTypeDefinition().FullName
                : i.FullName).ToList();

        if (interfaceNames.Contains(typeof(IDictionary<,>).FullName))
        {
            var dictionaryImpl = interfaces
                .First(i => i.IsGenericType
                            && i.GetGenericTypeDefinition().FullName == typeof(IDictionary<,>).FullName);

            var additionalProperties = dictionaryImpl.GenericTypeArguments[1].Map();
            return new V1JSONSchemaProps
            {
                Type = Object,
                AdditionalProperties = additionalProperties,
            };
        }

        if (interfaceNames.Contains(typeof(IDictionary).FullName))
        {
            return new V1JSONSchemaProps { Type = Object, XKubernetesPreserveUnknownFields = true };
        }

        if (interfaceNames.Contains(typeof(IEnumerable<>).FullName))
        {
            return type.MapEnumerationType(interfaces);
        }

        if (type.BaseType?.Name == nameof(CustomKubernetesEntity) || type.BaseType?.Name == typeof(CustomKubernetesEntity<>).Name)
        {
            return type.MapObjectType();
        }

        static Type GetRootBaseType(Type type)
        {
            var current = type;
            while (current.BaseType != null)
            {
                var baseName = current.BaseType.FullName;

                if (baseName == "System.Object" ||
                    baseName == "System.ValueType" ||
                    baseName == "System.Enum")
                {
                    return current.BaseType; // This is the root base we're after
                }

                current = current.BaseType;
            }

            return current; // In case it's already System.Object
        }

        var rootBase = GetRootBaseType(type);

        return rootBase.FullName switch
        {
            "System.Object" => type.MapObjectType(),
            "System.ValueType" => type.MapValueType(),
            "System.Enum" => new V1JSONSchemaProps
            {
                Type = String,
                EnumProperty = GetEnumNames(type),
            },
            _ => throw InvalidType(type),
        };
    }

    private static List<object> GetEnumNames(this Type type)
    {
#if NET9_0_OR_GREATER
        var attributeNameByFieldName = new Dictionary<string, string>();

        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetCustomAttribute<JsonStringEnumMemberNameAttribute>() is { Name: { } jsonMemberNameAtributeName })
            {
                attributeNameByFieldName.Add(field.Name, jsonMemberNameAtributeName);
            }
        }

        var enumNames = new List<object>();

        foreach (var value in Enum.GetNames(type))
        {
            enumNames.Add(attributeNameByFieldName.GetValueOrDefault(value, value));
        }

        return enumNames;
#else
        return Enum.GetNames(type).Cast<object>().ToList();
#endif
    }

    private static V1JSONSchemaProps MapObjectType(this Type type)
    {
        switch (type.FullName)
        {
            case "k8s.Models.V1ObjectMeta":
                return new V1JSONSchemaProps { Type = Object };
            case "k8s.Models.IntstrIntOrString":
                return new V1JSONSchemaProps { XKubernetesIntOrString = true };
            default:
                if (typeof(IKubernetesObject).IsAssignableFrom(type) &&
                    type is { IsAbstract: false, IsInterface: false } &&
                    type.Assembly == typeof(IKubernetesObject).Assembly)
                {
                    return new V1JSONSchemaProps
                    {
                        Type = Object,
                        Properties = null,
                        XKubernetesPreserveUnknownFields = true,
                        XKubernetesEmbeddedResource = true,
                    };
                }

                return new V1JSONSchemaProps
                {
                    Type = Object,
                    Description = type.GetCustomAttribute<DescriptionAttribute>()?.Description,
                    Properties = type
                        .GetProperties()
                        .Where(p => p.GetCustomAttribute<IgnoreAttribute>() == null)
                        .Select(p => (Name: p.GetPropertyName(), Schema: p.Map()))
                        .ToDictionary(t => t.Name, t => t.Schema),
                    Required = type.GetProperties()
                            .Where(p => p.GetCustomAttribute<RequiredAttribute>() != null
                                        && p.GetCustomAttribute<IgnoreAttribute>() == null)
                            .Select(p => p.GetPropertyName())
                            .ToList() switch
                    {
                        { Count: > 0 } p => p,
                        _ => null,
                    },
                    XKubernetesPreserveUnknownFields = type.GetCustomAttribute<PreserveUnknownFieldsAttribute>() != null ? true : null,
                };
        }
    }

    private static V1JSONSchemaProps MapEnumerationType(
        this Type type,
        IEnumerable<Type> interfaces)
    {
        Type? enumerableType = interfaces
            .FirstOrDefault(i => i.IsGenericType
                                 && i.GetGenericTypeDefinition().FullName == typeof(IEnumerable<>).FullName
                                 && i.GenericTypeArguments.Length == 1);

        if (enumerableType == null)
        {
            throw InvalidType(type);
        }

        Type listType = enumerableType.GenericTypeArguments[0];
        if (listType.IsGenericType && listType.GetGenericTypeDefinition().FullName == typeof(KeyValuePair<,>).FullName)
        {
            var additionalProperties = listType.GenericTypeArguments[1].Map();
            return new V1JSONSchemaProps
            {
                Type = Object,
                AdditionalProperties = additionalProperties,
            };
        }

        var items = listType.Map();
        return new V1JSONSchemaProps { Type = Array, Items = items };
    }

    private static V1JSONSchemaProps MapValueType(this Type type) =>
        type.FullName switch
        {
            "System.Int32" => new V1JSONSchemaProps { Type = Integer, Format = Int32 },
            "System.Int64" => new V1JSONSchemaProps { Type = Integer, Format = Int64 },
            "System.Single" => new V1JSONSchemaProps { Type = Number, Format = Float },
            "System.Double" => new V1JSONSchemaProps { Type = Number, Format = Double },
            "System.Decimal" => new V1JSONSchemaProps { Type = Number, Format = Decimal },
            "System.Boolean" => new V1JSONSchemaProps { Type = Boolean },
            "System.DateTime" => new V1JSONSchemaProps { Type = String, Format = DateTime },
            "System.DateTimeOffset" => new V1JSONSchemaProps { Type = String, Format = DateTime },
            _ => throw InvalidType(type),
        };

    private static ArgumentException InvalidType(Type type) =>
        new($"The given type {type.FullName} is not a valid Kubernetes entity.");
}
