using System.Text.RegularExpressions;
using Constructs;

namespace CrossyRoad.Aws;

public sealed record DeploymentSettings(
    string ProjectName,
    string EnvironmentName,
    string Region,
    string? Account)
{
    public const string DefaultProjectName = "CrossyRoad";
    public const string DefaultEnvironmentName = "production";
    public const string DefaultRegion = "ap-northeast-1";

    private static readonly Regex NamePattern = new(
        "^[A-Za-z][A-Za-z0-9-]{1,31}$",
        RegexOptions.CultureInvariant);

    private static readonly Regex RegionPattern = new(
        "^[a-z]{2}(?:-gov)?-[a-z]+-\\d$",
        RegexOptions.CultureInvariant);

    private static readonly Regex AccountPattern = new(
        "^\\d{12}$",
        RegexOptions.CultureInvariant);

    public static DeploymentSettings Production { get; } = new(
        DefaultProjectName,
        DefaultEnvironmentName,
        DefaultRegion,
        Account: null);

    public static DeploymentSettings FromContext(Node node)
    {
        ArgumentNullException.ThrowIfNull(node);

        var settings = new DeploymentSettings(
            ReadContext(node, "projectName") ?? DefaultProjectName,
            ReadContext(node, "environmentName") ?? DefaultEnvironmentName,
            ReadContext(node, "region") ?? DefaultRegion,
            Environment.GetEnvironmentVariable("CDK_DEFAULT_ACCOUNT"));

        settings.EnsureValid();
        return settings;
    }

    public string StackName(string component)
    {
        if (string.IsNullOrWhiteSpace(component))
        {
            throw new ArgumentException(
                "A stack component is required.",
                nameof(component));
        }

        return $"{ProjectName}{component}Stack-{EnvironmentName}";
    }

    public void EnsureValid()
    {
        if (!NamePattern.IsMatch(ProjectName))
        {
            throw new ArgumentException(
                "ProjectName must start with a letter and contain only " +
                "letters, numbers, or hyphens.",
                nameof(ProjectName));
        }

        if (!string.Equals(
                EnvironmentName,
                DefaultEnvironmentName,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Only the production environment is defined at this stage.",
                nameof(EnvironmentName));
        }

        if (!RegionPattern.IsMatch(Region))
        {
            throw new ArgumentException(
                "Region must be a valid AWS region identifier.",
                nameof(Region));
        }

        if (Account is not null && !AccountPattern.IsMatch(Account))
        {
            throw new ArgumentException(
                "Account must be a 12-digit AWS account ID.",
                nameof(Account));
        }
    }

    private static string? ReadContext(Node node, string key)
    {
        object? value = node.TryGetContext(key);
        return value?.ToString()?.Trim() is { Length: > 0 } text
            ? text
            : null;
    }
}
