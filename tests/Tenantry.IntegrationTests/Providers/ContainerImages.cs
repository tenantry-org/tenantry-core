namespace Tenantry.IntegrationTests.Providers;

/// <summary>
/// The database images the integration tests run against, pinned to a release so that a new image cannot change a
/// run. Update them here.
/// </summary>
internal static class ContainerImages
{
    public const string SqlServer = "mcr.microsoft.com/mssql/server:2022-CU27-ubuntu-22.04";

    public const string PostgreSql = "postgres:16.15-alpine3.24";

    public const string MySql = "mysql:8.4.11";
}
