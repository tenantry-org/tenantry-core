using Microsoft.Extensions.Primitives;

namespace Tenantry.AspNetCore.Internal;

internal static class RequestValues
{
    /// <summary>
    /// The identifier a header or query parameter carries: its value, trimmed, when it has exactly one that is not
    /// blank. A repeated header or parameter names no tenant, so a value a client added beside the one a proxy appended
    /// cannot be taken in its place.
    /// </summary>
    public static ValueTask<string?> Single(StringValues values) =>
        new(values.Count == 1 && !string.IsNullOrWhiteSpace(values[0]) ? values[0]!.Trim() : null);
}
