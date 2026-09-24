using System.Reflection;
using CounterService.Domain;
using Jev.Client;

namespace CoffeeShop.Tests;

/// <summary>
/// tasks.md T16 (research.md's VSA invariants, §6/§14 Q11). Plain reflection over each type's
/// public API surface (base type, properties, fields, method/ctor parameters and return types)
/// - no new package, matching the vertical-slice-architecture skill's own approach.
/// </summary>
public class ArchitectureTests
{
    private static readonly string[] ForbiddenDomainNamespaces =
    [
        "Microsoft.AspNetCore",
        "Microsoft.Agents",
        "ModelContextProtocol",
        "CounterService.Features",
    ];

    [Fact]
    public void Domain_DoesNotReference_WebOrAgentOrMcpOrFeatures()
    {
        var offenders = TypesInNamespace("CounterService.Domain")
            .SelectMany(t => ReferencedTypeNamespaces(t).Select(ns => (Type: t, Namespace: ns)))
            .Where(x => ForbiddenDomainNamespaces.Any(f => x.Namespace!.StartsWith(f, StringComparison.Ordinal)))
            .Select(x => $"{x.Type.FullName} -> {x.Namespace}")
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void FeaturesMenu_And_FeaturesOrders_NeverReferenceEachOther()
    {
        AssertNoCrossReference("CounterService.Features.Menu", "CounterService.Features.Orders");
        AssertNoCrossReference("CounterService.Features.Orders", "CounterService.Features.Menu");
    }

    [Fact]
    public void JevClientAssembly_ReferencesNoCoffeeShopAssembly()
    {
        var jevAssembly = typeof(JevClient).Assembly;
        var offenders = jevAssembly.GetReferencedAssemblies()
            .Where(a => a.Name is not null && (a.Name.StartsWith("CoffeeShop", StringComparison.Ordinal) || a.Name is "CounterService" or "ProductCatalogService"))
            .Select(a => a.Name)
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void NoPriceOrTotalProperty_IsFloatOrDouble()
    {
        var offenders = typeof(OrderLine).Assembly.GetTypes()
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(p =>
                (p.Name.Contains("Price", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("Total", StringComparison.OrdinalIgnoreCase)) &&
                (p.PropertyType == typeof(float) || p.PropertyType == typeof(double)))
            .Select(p => $"{p.DeclaringType!.FullName}.{p.Name}")
            .ToList();

        Assert.Empty(offenders);
    }

    private static void AssertNoCrossReference(string fromNamespace, string intoNamespace)
    {
        var offenders = TypesInNamespace(fromNamespace)
            .SelectMany(t => ReferencedTypeNamespaces(t).Select(ns => (Type: t, Namespace: ns)))
            .Where(x => x.Namespace!.StartsWith(intoNamespace, StringComparison.Ordinal))
            .Select(x => $"{x.Type.FullName} -> {x.Namespace}")
            .ToList();

        Assert.Empty(offenders);
    }

    private static IEnumerable<Type> TypesInNamespace(string ns) =>
        typeof(OrderLine).Assembly.GetTypes().Where(t => t.Namespace == ns);

    /// <summary>The namespaces of every type this type's public surface mentions: base type,
    /// interfaces, properties, fields, and its own declared methods'/constructors' parameters
    /// and return types. A body-level (local variable) reference is not inspected - this is a
    /// surface-level check, matching the vertical-slice-architecture skill's own approach.</summary>
    private static IEnumerable<string> ReferencedTypeNamespaces(Type type)
    {
        var referenced = new List<Type?> { type.BaseType };
        referenced.AddRange(type.GetInterfaces());
        referenced.AddRange(type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).Select(p => p.PropertyType));
        referenced.AddRange(type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).Select(f => f.FieldType));

        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            referenced.Add(method.ReturnType);
            referenced.AddRange(method.GetParameters().Select(p => p.ParameterType));
        }

        foreach (var ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            referenced.AddRange(ctor.GetParameters().Select(p => p.ParameterType));
        }

        return referenced
            .Where(t => t is not null)
            .Select(t => UnwrapGenericArguments(t!))
            .SelectMany(t => t)
            .Select(t => t.Namespace)
            .Where(ns => ns is not null)
            .Distinct()!;
    }

    /// <summary>A generic like <c>List&lt;OrderLine&gt;</c> or <c>Task&lt;OrderLine&gt;</c> must
    /// surface its type argument's namespace too, not just "System.Collections.Generic".</summary>
    private static IEnumerable<Type> UnwrapGenericArguments(Type type)
    {
        yield return type;
        if (type.IsGenericType)
        {
            foreach (var arg in type.GetGenericArguments())
            {
                foreach (var inner in UnwrapGenericArguments(arg))
                {
                    yield return inner;
                }
            }
        }
    }
}
