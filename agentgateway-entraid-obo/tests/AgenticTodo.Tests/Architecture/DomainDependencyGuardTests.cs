using System.Reflection;
using AgenticTodo.TodoAgent.Domain;
using AgenticTodo.TodoMcpServer.Domain;
using Xunit;

namespace AgenticTodo.Tests.Architecture;

// GUD-003: *.Domain namespaces stay I/O-free -- no Microsoft.Identity, EF Core,
// System.Net.Http, or MCP SDK types on any Domain type's public/private surface.
public class DomainDependencyGuardTests
{
    private static readonly string[] ForbiddenNamespacePrefixes =
    [
        "Microsoft.Identity",
        "Microsoft.EntityFrameworkCore",
        "System.Net.Http",
        "ModelContextProtocol",
    ];

    public static IEnumerable<object[]> DomainAssemblies()
    {
        yield return new object[] { typeof(TodoResult).Assembly, "AgenticTodo.TodoAgent.Domain" };
        yield return new object[] { typeof(Todo).Assembly, "AgenticTodo.TodoMcpServer.Domain" };
    }

    [Theory]
    [MemberData(nameof(DomainAssemblies))]
    public void Domain_types_reference_no_forbidden_infrastructure_namespace(Assembly assembly, string domainNamespacePrefix)
    {
        var domainTypes = assembly.GetTypes()
            .Where(type => type.Namespace is not null && type.Namespace.StartsWith(domainNamespacePrefix, StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(domainTypes);

        var violations = new List<string>();
        foreach (var domainType in domainTypes)
        {
            foreach (var referencedType in ReferencedTypes(domainType).SelectMany(Flatten))
            {
                var referencedNamespace = referencedType.Namespace;
                if (referencedNamespace is null)
                {
                    continue;
                }

                var forbidden = ForbiddenNamespacePrefixes.FirstOrDefault(prefix =>
                    referencedNamespace.StartsWith(prefix, StringComparison.Ordinal));
                if (forbidden is not null)
                {
                    violations.Add($"{domainType.FullName} references {referencedType.FullName} ({forbidden})");
                }
            }
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    private static IEnumerable<Type> ReferencedTypes(Type type)
    {
        if (type.BaseType is not null)
        {
            yield return type.BaseType;
        }

        foreach (var interfaceType in type.GetInterfaces())
        {
            yield return interfaceType;
        }

        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (var field in type.GetFields(flags))
        {
            yield return field.FieldType;
        }

        foreach (var property in type.GetProperties(flags))
        {
            yield return property.PropertyType;
        }

        foreach (var method in type.GetMethods(flags))
        {
            yield return method.ReturnType;
            foreach (var parameter in method.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }

        foreach (var constructor in type.GetConstructors(flags))
        {
            foreach (var parameter in constructor.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }
    }

    private static IEnumerable<Type> Flatten(Type type)
    {
        yield return type;

        if (!type.IsGenericType)
        {
            yield break;
        }

        foreach (var argument in type.GetGenericArguments())
        {
            foreach (var nested in Flatten(argument))
            {
                yield return nested;
            }
        }
    }
}
