namespace Tenantry.AspNetCore;

/// <summary>
/// The names <c>app.UseTenantry()</c> records requests under, for OpenTelemetry's <c>AddSource</c> and
/// <c>AddMeter</c>, and for log filters. The request's tenant itself goes under <see cref="TenantTelemetry"/>'s names.
/// </summary>
public static class TenantryAspNetCoreTelemetry
{
    /// <summary>The activity source of the <c>Tenantry.ResolveTenant</c> span: <c>Tenantry.AspNetCore</c>.</summary>
    public const string ActivitySourceName = "Tenantry.AspNetCore";

    /// <summary>The meter of the <c>tenantry.resolutions</c> counter: <c>Tenantry.AspNetCore</c>.</summary>
    public const string MeterName = "Tenantry.AspNetCore";

    /// <summary>The category of the middleware's log messages: <c>Tenantry.AspNetCore</c>.</summary>
    public const string LogCategory = "Tenantry.AspNetCore";
}
