using Microsoft.Extensions.Logging;

namespace Tenantry.Options.Internal;

/// <summary>
/// Tenantry.Options' log messages, under the category <c>Tenantry.Options</c>. Their event ids are stable: the
/// documentation lists them, so applications can alert on them.
/// </summary>
internal static partial class TenantOptionsLog
{
    public const string Category = "Tenantry.Options";

    [LoggerMessage(3001, LogLevel.Warning,
        "IOptions<{OptionsType}> was read while tenant {TenantId} is current. It gives the ordinary value, not the " +
        "tenant's: read IOptionsSnapshot<{OptionsType}> or IOptionsMonitor<{OptionsType}> for the tenant's value. " +
        "Logged once per options type",
        EventName = "OrdinaryOptionsReadAsTenant")]
    public static partial void OrdinaryOptionsReadAsTenant(ILogger logger, string optionsType, string tenantId);
}
