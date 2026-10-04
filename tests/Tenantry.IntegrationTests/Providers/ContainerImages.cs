namespace Tenantry.IntegrationTests.Providers;

/// <summary>
/// The database images the integration tests run against: the releases pinned in <c>Providers/compose.yml</c>, or the
/// image an environment variable names (<c>TENANTRY_SQLSERVER_IMAGE</c>, <c>TENANTRY_POSTGRES_IMAGE</c>,
/// <c>TENANTRY_MYSQL_IMAGE</c>), as the weekly dependency lane sets to run against the newest releases.
/// </summary>
internal static class ContainerImages
{
    private static readonly Dictionary<string, string> Pinned = ReadPinned();

    public static string SqlServer { get; } = Image("sqlserver", "TENANTRY_SQLSERVER_IMAGE");

    public static string PostgreSql { get; } = Image("postgres", "TENANTRY_POSTGRES_IMAGE");

    public static string MySql { get; } = Image("mysql", "TENANTRY_MYSQL_IMAGE");

    private static string Image(string service, string variable) =>
        Environment.GetEnvironmentVariable(variable) is { Length: > 0 } image
            ? image
            : Pinned.GetValueOrDefault(service) ?? throw new InvalidOperationException($"Providers/compose.yml names no image for '{service}'.");

    // Each service's `image:` line, under its name at the second level of the file.
    private static Dictionary<string, string> ReadPinned()
    {
        Dictionary<string, string> images = [];
        string? service = null;

        foreach (var line in File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Providers", "compose.yml")))
        {
            var trimmed = line.Trim();

            if (line.StartsWith("  ", StringComparison.Ordinal) && !line.StartsWith("   ", StringComparison.Ordinal) && trimmed.EndsWith(':'))
            {
                service = trimmed[..^1];
            }
            else if (service is not null && trimmed.StartsWith("image:", StringComparison.Ordinal))
            {
                images[service] = trimmed["image:".Length..].Trim();
            }
        }

        return images;
    }
}
