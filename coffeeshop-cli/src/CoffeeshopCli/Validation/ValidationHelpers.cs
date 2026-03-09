using System.Text.RegularExpressions;

namespace CoffeeshopCli.Validation;

/// <summary>
/// DRY helper for common validation patterns (emails, patterns, ranges).
/// </summary>
public static class ValidationHelpers
{
    private static readonly Regex EmailRegex = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    private static readonly Regex PhoneRegex = new(
        @"^\+?[\d\s\-\(\)]+$",
        RegexOptions.Compiled
    );

    /// <summary>
    /// Validate email format.
    /// </summary>
    public static bool IsValidEmail(string? email)
    {
        return !string.IsNullOrWhiteSpace(email) && EmailRegex.IsMatch(email);
    }

    /// <summary>
    /// Validate phone number format (very permissive).
    /// </summary>
    public static bool IsValidPhone(string? phone)
    {
        return !string.IsNullOrWhiteSpace(phone) && PhoneRegex.IsMatch(phone);
    }

    /// <summary>
    /// Validate against a regex pattern.
    /// </summary>
    public static bool MatchesPattern(string? value, string pattern)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var regex = new Regex(pattern, RegexOptions.IgnoreCase);
        return regex.IsMatch(value);
    }

    /// <summary>
    /// Validate that an integer is within a specified range (inclusive).
    /// </summary>
    public static bool IsInRange(int value, int min, int max)
    {
        return value >= min && value <= max;
    }

    /// <summary>
    /// Validate that a decimal is positive.
    /// </summary>
    public static bool IsPositive(decimal value)
    {
        return value > 0;
    }
}
