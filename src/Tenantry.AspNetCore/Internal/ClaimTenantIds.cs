using System.Security.Claims;
using System.Text.Json;

namespace Tenantry.AspNetCore.Internal;

/// <summary>
/// The tenant ids a user's claims of one type list, read the same way by <see cref="ClaimTenantResolver"/> and
/// <c>ValidateTenantAccessByClaim</c>: each claim's value, or each string or number of a claim that holds a JSON array
/// (<c>["acme","globex"]</c>). Values are trimmed, and blank ones list no tenant.
/// </summary>
internal static class ClaimTenantIds
{
    public static IEnumerable<string> Read(ClaimsPrincipal user, string claimType) =>
        user.FindAll(claimType).SelectMany(claim => Ids(claim.Value.Trim()));

    private static string[] Ids(string value)
    {
        if (value.Length == 0)
            return [];

        if (!value.StartsWith('['))
            return [value];

        try
        {
            // Valid JSON that starts with '[' is an array.
            using var document = JsonDocument.Parse(value);
            return [.. document.RootElement.EnumerateArray().Select(Id).OfType<string>()];
        }
        catch (JsonException)
        {
            // Text that starts with '[' but is not valid JSON is one id, as any other value is.
            return [value];
        }
    }

    private static string? Id(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString()?.Trim() is { Length: > 0 } id ? id : null,
        JsonValueKind.Number => element.GetRawText(),
        _ => null,
    };
}
