using CoffeeshopCli.Models;
using CoffeeshopCli.Validation;
using Xunit;

namespace CoffeeshopCli.Tests.Validation;

public class CustomerValidatorTests
{
    private readonly CustomerValidator _validator = new();

    [Fact]
    public void Validate_ValidCustomer_ReturnsNull()
    {
        var customer = new Customer
        {
            CustomerId = "C-1001",
            Email = "alice@example.com",
            Name = "Alice Smith",
            Phone = "+1-555-0100",
            Tier = CustomerTier.Gold,
            AccountCreated = new DateOnly(2024, 1, 15)
        };

        var result = _validator.Validate(customer);

        Assert.Null(result);
    }

    [Fact]
    public void Validate_InvalidCustomerId_ReturnsError()
    {
        var customer = new Customer
        {
            CustomerId = "INVALID",
            Email = "alice@example.com",
            Name = "Alice Smith",
            Phone = "+1-555-0100",
            Tier = CustomerTier.Gold,
            AccountCreated = new DateOnly(2024, 1, 15)
        };

        var result = _validator.Validate(customer);

        Assert.NotNull(result);
        var errors = GetErrorList(result!);
        Assert.Contains(errors, e => e.Contains("CustomerId must match pattern"));
    }

    [Fact]
    public void Validate_InvalidEmail_ReturnsError()
    {
        var customer = new Customer
        {
            CustomerId = "C-1001",
            Email = "not-an-email",
            Name = "Alice Smith",
            Phone = "+1-555-0100",
            Tier = CustomerTier.Gold,
            AccountCreated = new DateOnly(2024, 1, 15)
        };

        var result = _validator.Validate(customer);

        Assert.NotNull(result);
        Assert.Equal("Customer validation failed", result!.Message);
        Assert.NotNull(result.Details);
        Assert.True(result.Details.ContainsKey("errors"));
    }

    [Fact]
    public void Validate_EmptyName_ReturnsError()
    {
        var customer = new Customer
        {
            CustomerId = "C-1001",
            Email = "alice@example.com",
            Name = "",
            Phone = "+1-555-0100",
            Tier = CustomerTier.Gold,
            AccountCreated = new DateOnly(2024, 1, 15)
        };

        var result = _validator.Validate(customer);

        Assert.NotNull(result);
        Assert.Contains("Name is required", GetErrorList(result!));
    }

    private static List<string> GetErrorList(Errors.ValidationError error)
    {
        if (error.Details != null && error.Details.TryGetValue("errors", out var errors))
        {
            return errors as List<string> ?? new List<string>();
        }
        return new List<string>();
    }
}
