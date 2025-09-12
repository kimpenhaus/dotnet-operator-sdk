// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Reflection;

namespace KubeOps.Transpiler;

public static class Utilities
{
    /// <summary>
    /// Überprüft, ob ein Typ ein nullfähiger Werttyp ist (Nullable&lt;T&gt;).
    /// </summary>
    /// <param name="type">Der Typ.</param>
    /// <returns>True, wenn der Typ nullfähig ist.</returns>
    public static bool IsNullable(this Type type)
        => Nullable.GetUnderlyingType(type) != null;

    /// <summary>
    /// Überprüft, ob eine Eigenschaft nullfähig ist.
    /// Dies funktioniert sowohl für nullfähige Werttypen (z. B. int?) als auch für nullfähige Verweistypen (z. B. string?).
    /// </summary>
    /// <param name="prop">Die Eigenschaft.</param>
    /// <returns>True, wenn die Eigenschaft nullfähig ist.</returns>
    public static bool IsNullable(this PropertyInfo prop)
        => new NullabilityInfoContext().Create(prop).ReadState == NullabilityState.Nullable;

    /// <summary>
    /// Load a type from a metadata load context.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <typeparam name="T">The type.</typeparam>
    /// <returns>The loaded reflected type.</returns>
    public static Type GetContextType<T>(this MetadataLoadContext context)
        => context.GetContextType(typeof(T));

    /// <summary>
    /// Load a type from a metadata load context.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="type">The type.</param>
    /// <returns>The loaded reflected type.</returns>
    public static Type GetContextType(this MetadataLoadContext context, Type type)
    {
        foreach (var assembly in context.GetAssemblies())
        {
            if (assembly.GetType(type.FullName!) is { } t)
            {
                return t;
            }
        }

        var newAssembly = context.LoadFromAssemblyPath(type.Assembly.Location);
        return newAssembly.GetType(type.FullName!)!;
    }
}
