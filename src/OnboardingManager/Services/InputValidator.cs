using System.Text.RegularExpressions;

namespace OnboardingManager.Services;

public static partial class InputValidator
{
    [GeneratedRegex("^[A-Za-z0-9](?:[A-Za-z0-9-]{0,13}[A-Za-z0-9])?$")]
    private static partial Regex HostNameRegex();

    [GeneratedRegex("^(?=.{1,253}$)(?:[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?\\.)+[A-Za-z]{2,63}$")]
    private static partial Regex DomainRegex();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{1,200}$")]
    private static partial Regex PackageIdRegex();

    public static string? ValidateComputerName(string value)
    {
        var name = value.Trim();
        if (name.Length is < 1 or > 15)
            return "The computer name must be between 1 and 15 characters long.";
        if (!HostNameRegex().IsMatch(name))
            return "The name may contain letters, numbers, and hyphens; it cannot begin or end with a hyphen.";
        if (name.All(char.IsDigit))
            return "The computer name cannot consist only of numbers.";
        return null;
    }

    public static string? ValidateDomainName(string value)
    {
        if (!DomainRegex().IsMatch(value.Trim()))
            return "Enter the fully qualified domain name, for example corp.local or ad.example.com.";
        return null;
    }

    public static string? ValidateOuPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!value.TrimStart().StartsWith("OU=", StringComparison.OrdinalIgnoreCase) ||
            !value.Contains("DC=", StringComparison.OrdinalIgnoreCase))
            return "The OU path must use this format: OU=Computers,DC=corp,DC=local.";
        return null;
    }

    public static bool IsValidPackageId(string value) => PackageIdRegex().IsMatch(value.Trim());
}

