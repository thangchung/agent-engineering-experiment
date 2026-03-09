using System.ComponentModel.DataAnnotations;
using System.Reflection;
using CoffeeshopCli.Models;

namespace CoffeeshopCli.Services;

/// <summary>
/// Registry for domain models with reflection-based introspection.
/// Discovers C# record types and extracts property metadata.
/// </summary>
public sealed class ModelRegistry
{
    private readonly Dictionary<string, Type> _models = new();

    public ModelRegistry()
    {
        // Register all model types from the Models namespace
        var modelTypes = typeof(Customer).Assembly.GetTypes()
            .Where(t => t.Namespace == "CoffeeshopCli.Models" 
                && t.IsClass 
                && !t.IsAbstract
                && t.Name != "Enums");

        foreach (var type in modelTypes)
        {
            _models[type.Name] = type;
        }
    }

    public IReadOnlyList<string> GetModelNames()
    {
        return _models.Keys.OrderBy(n => n).ToList();
    }

    public Type? GetModelType(string name)
    {
        _models.TryGetValue(name, out var type);
        return type;
    }

    public ModelSchema GetSchema(string name)
    {
        if (!_models.TryGetValue(name, out var type))
        {
            throw new KeyNotFoundException($"Model '{name}' not found");
        }

        return new ModelSchema
        {
            Name = name,
            Properties = GetProperties(type)
        };
    }

    private List<PropertySchema> GetProperties(Type type)
    {
        var properties = new List<PropertySchema>();

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var schema = new PropertySchema
            {
                Name = prop.Name,
                TypeName = GetFriendlyTypeName(prop.PropertyType),
                IsRequired = prop.GetCustomAttribute<RequiredAttribute>() != null,
                IsNullable = IsNullableType(prop.PropertyType),
                Attributes = GetAttributeInfo(prop)
            };

            // Handle nested types
            if (IsComplexType(prop.PropertyType))
            {
                var elementType = GetElementType(prop.PropertyType);
                if (elementType != null && elementType.Namespace == "CoffeeshopCli.Models")
                {
                    schema.ChildProperties = GetProperties(elementType);
                }
            }

            // Handle enums
            if (prop.PropertyType.IsEnum)
            {
                schema.EnumValues = Enum.GetNames(prop.PropertyType).ToList();
            }
            else if (IsNullableEnum(prop.PropertyType, out var enumType))
            {
                schema.EnumValues = Enum.GetNames(enumType!).ToList();
            }

            properties.Add(schema);
        }

        return properties;
    }

    private static string GetFriendlyTypeName(Type type)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            var elementType = type.GetGenericArguments()[0];
            return $"List<{elementType.Name}>";
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            var underlyingType = Nullable.GetUnderlyingType(type);
            return $"{underlyingType?.Name}?";
        }

        return type.Name;
    }

    private static bool IsNullableType(Type type)
    {
        return Nullable.GetUnderlyingType(type) != null;
    }

    private static bool IsNullableEnum(Type type, out Type? enumType)
    {
        enumType = Nullable.GetUnderlyingType(type);
        return enumType?.IsEnum ?? false;
    }

    private static bool IsComplexType(Type type)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            return true;
        }

        return type.IsClass && type != typeof(string) && type.Namespace == "CoffeeshopCli.Models";
    }

    private static Type? GetElementType(Type type)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            return type.GetGenericArguments()[0];
        }

        return null;
    }

    private static Dictionary<string, string> GetAttributeInfo(PropertyInfo prop)
    {
        var attrs = new Dictionary<string, string>();

        if (prop.GetCustomAttribute<RequiredAttribute>() is not null)
        {
            attrs["Required"] = "true";
        }

        if (prop.GetCustomAttribute<EmailAddressAttribute>() is not null)
        {
            attrs["EmailAddress"] = "true";
        }

        if (prop.GetCustomAttribute<PhoneAttribute>() is not null)
        {
            attrs["Phone"] = "true";
        }

        if (prop.GetCustomAttribute<RangeAttribute>() is RangeAttribute range)
        {
            attrs["Range"] = $"{range.Minimum} - {range.Maximum}";
        }

        if (prop.GetCustomAttribute<RegularExpressionAttribute>() is RegularExpressionAttribute regex)
        {
            attrs["Pattern"] = regex.Pattern;
        }

        return attrs;
    }
}

public record ModelSchema
{
    public required string Name { get; init; }
    public required List<PropertySchema> Properties { get; init; }
}

public record PropertySchema
{
    public required string Name { get; init; }
    public required string TypeName { get; init; }
    public required bool IsRequired { get; init; }
    public required bool IsNullable { get; init; }
    public required Dictionary<string, string> Attributes { get; init; }
    public List<PropertySchema>? ChildProperties { get; set; }
    public List<string>? EnumValues { get; set; }
}
