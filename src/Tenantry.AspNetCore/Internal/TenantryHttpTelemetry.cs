using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Tenantry.AspNetCore.Internal;

/// <summary>
/// The names <c>app.UseTenantry()</c> records under: an activity source and a meter named <c>Tenantry.AspNetCore</c>,
/// and its resolution tags. The request's tenant goes under <see cref="TenantTelemetry"/>'s names, which Tenantry.Pro's
/// jobs and messages use too.
/// </summary>
internal static class TenantryHttpTelemetry
{
    public const string Name = TenantryAspNetCoreTelemetry.ActivitySourceName;

    public const string ResolveActivityName = "Tenantry.ResolveTenant";

    public const string ResultTag = "tenantry.resolution.result";

    public const string RejectedTag = "tenantry.resolution.rejected";

    public static readonly ActivitySource ActivitySource = new(Name);

    public static string ResultName(ResolutionResult result) =>
        result switch
        {
            ResolutionResult.Resolved => "resolved",
            ResolutionResult.Missing => "missing",
            ResolutionResult.NotFound => "not_found",
            ResolutionResult.Inactive => "inactive",
            _ => "access_denied",
        };
}

/// <summary>
/// What <c>app.UseTenantry()</c> found for a request.
/// </summary>
internal enum ResolutionResult
{
    Resolved,
    Missing,
    NotFound,
    AccessDenied,
    Inactive,
}

/// <summary>
/// Counts the requests <c>app.UseTenantry()</c> handles, by result, on the <c>Tenantry.AspNetCore</c> meter.
/// </summary>
internal sealed class TenantResolutionMetrics
{
    private readonly Counter<long> _resolutions;

    // A host registers IMeterFactory (AddMetrics); without one, the meter is the process's own.
    public TenantResolutionMetrics(IMeterFactory? meterFactory)
    {
        var meter = meterFactory?.Create(TenantryAspNetCoreTelemetry.MeterName) ?? new Meter(TenantryAspNetCoreTelemetry.MeterName);

        _resolutions = meter.CreateCounter<long>(
            "tenantry.resolutions",
            unit: "{request}",
            description: "Requests app.UseTenantry() resolved to a tenant, or did not, by result.");
    }

    public void Record(ResolutionResult result, bool rejected)
    {
        if (_resolutions.Enabled)
        {
            _resolutions.Add(
                1,
                new KeyValuePair<string, object?>(TenantryHttpTelemetry.ResultTag, TenantryHttpTelemetry.ResultName(result)),
                new KeyValuePair<string, object?>(TenantryHttpTelemetry.RejectedTag, rejected));
        }
    }
}
