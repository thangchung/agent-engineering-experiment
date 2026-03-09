using CoffeeshopCli.Models;
using CoffeeshopCli.Services;
using Xunit;

namespace CoffeeshopCli.Tests.Services;

public class ModelRegistryTests
{
    private readonly ModelRegistry _registry = new();

    [Fact]
    public void GetModelNames_ReturnsAllModels()
    {
        // Act
        var names = _registry.GetModelNames();

        // Assert
        Assert.Contains("Customer", names);
        Assert.Contains("MenuItem", names);
        Assert.Contains("Order", names);
        Assert.Contains("OrderItem", names);
        Assert.Contains("OrderNote", names);
        Assert.Equal(5, names.Count);
    }

    [Fact]
    public void GetModelType_WithValidName_ReturnsType()
    {
        // Act
        var type = _registry.GetModelType("Customer");

        // Assert
        Assert.NotNull(type);
        Assert.Equal(typeof(Customer), type);
    }

    [Fact]
    public void GetModelType_WithInvalidName_ReturnsNull()
    {
        // Act
        var type = _registry.GetModelType("NonExistent");

        // Assert
        Assert.Null(type);
    }

    [Fact]
    public void GetSchema_WithOrder_ReturnsSchemaWithNestedTypes()
    {
        // Act
        var schema = _registry.GetSchema("Order");

        // Assert
        Assert.Equal("Order", schema.Name);
        Assert.NotEmpty(schema.Properties);

        var itemsProperty = schema.Properties.FirstOrDefault(p => p.Name == "Items");
        Assert.NotNull(itemsProperty);
        Assert.Equal("List<OrderItem>", itemsProperty.TypeName);
        Assert.NotNull(itemsProperty.ChildProperties);
        Assert.NotEmpty(itemsProperty.ChildProperties);
    }

    [Fact]
    public void GetSchema_WithCustomer_ReturnsPropertiesWithAttributes()
    {
        // Act
        var schema = _registry.GetSchema("Customer");

        // Assert
        var customerIdProperty = schema.Properties.FirstOrDefault(p => p.Name == "CustomerId");
        Assert.NotNull(customerIdProperty);
        Assert.True(customerIdProperty.IsRequired);
        Assert.Contains("Pattern", customerIdProperty.Attributes.Keys);
        Assert.Contains("C-\\d{4}", customerIdProperty.Attributes["Pattern"]);
    }

    [Fact]
    public void GetSchema_WithEnums_ReturnsEnumValues()
    {
        // Act
        var schema = _registry.GetSchema("Order");

        // Assert
        var statusProperty = schema.Properties.FirstOrDefault(p => p.Name == "Status");
        Assert.NotNull(statusProperty);
        Assert.NotNull(statusProperty.EnumValues);
        Assert.Equal(6, statusProperty.EnumValues.Count);
        Assert.Contains("Pending", statusProperty.EnumValues);
        Assert.Contains("Completed", statusProperty.EnumValues);
    }

    [Fact]
    public void GetSchema_WithInvalidName_ThrowsKeyNotFoundException()
    {
        // Act & Assert
        Assert.Throws<KeyNotFoundException>(() => _registry.GetSchema("NonExistent"));
    }
}
