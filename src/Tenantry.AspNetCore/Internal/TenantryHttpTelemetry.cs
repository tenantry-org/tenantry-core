using System.Collections;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;

namespace Tenantry.AspNetCore.Internal;

/// <summary>
/// The names <c>app.UseTenantry()</c> records under: an activity source and a meter named <c>Tenantry.AspNetCore</c>,
/// the request's <c>tenant.id</c> tag and its <c>TenantId</c> log scope. Tenantry.Pro's jobs and messages use the
/// same tag and scope names.
/// </summary>
internal static class TenantryHttpTelemetry
{
    public const string Name = "Tenantry.AspNetCore";

    public const string ResolveActivityName = "Tenantry.ResolveTenant";

    public const string TenantIdTag = "tenant.id";

    public const string ResultTag = "tenantry.resolution.result";

    public const string RejectedTag = "tenantry.resolution.rejected";

    public const string LogScopeName = "TenantId";

    public static readonly ActivitySource ActivitySource = new(Name);

    // The tenant id as jobs and messages carry it: formatted with the invariant culture.
    public static string Format<TKey>(TKey tenantId) =>
        (tenantId is IFormattable formattable
            ? formattable.ToString(null, CultureInfo.InvariantCulture)
            : tenantId?.ToString()) ?? string.Empty;

    public static string ResultName(ResolutionResult result) =>
        result switch
        {
            ResolutionResult.Resolved => "resolved",
            ResolutionResult.Missing => "missing",
            ResolutionResult.NotFound => "not_found",
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
        var meter = meterFactory?.Create(TenantryHttpTelemetry.Name) ?? new Meter(TenantryHttpTelemetry.Name);

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

/// <summary>
/// The log scope a request's tenant opens: one property, <c>TenantId</c>.
/// </summary>
internal sealed class TenantLogScope(string tenantId) : IReadOnlyList<KeyValuePair<string, object?>>
{
    public int Count => 1;

    public KeyValuePair<string, object?> this[int index] =>
        index == 0
            ? new KeyValuePair<string, object?>(TenantryHttpTelemetry.LogScopeName, tenantId)
            : throw new ArgumentOutOfRangeException(nameof(index));

    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
    {
        yield return this[0];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() => $"{TenantryHttpTelemetry.LogScopeName}:{tenantId}";
}
