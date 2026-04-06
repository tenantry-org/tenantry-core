using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Tenantry.Core;

namespace Tenantry.AspNetCore.Internal;

internal static class ClaimTenantAccessValidator
{
    public static ValueTask<bool> ValidateAsync<TKey>(
        HttpContext httpContext,
        ITenantDescriptor<TKey> tenant,
        string claimType)
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        return ValueTask.FromResult(httpContext.User
            .FindAll(claimType)
            .Select(c => c.Value)
            .Any(claimValue => ClaimMatchesTenant(claimValue, tenant.TenantId)));
    }

    private static bool ClaimMatchesTenant<TKey>(string claimValue, TKey tenantId)
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        var trimmed = claimValue.Trim();

        if (TryParseTenantId(trimmed, out TKey? parsedTenantId) &&
            EqualityComparer<TKey>.Default.Equals(parsedTenantId, tenantId))
        {
            return true;
        }

        if (!trimmed.StartsWith('['))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);

            // Valid JSON starting with '[' is always an array; EnumerateArray
            // would throw JsonException for any other root kind, caught below.
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (!TryGetCandidateValue(element, out var candidateValue) ||
                    candidateValue is null ||
                    !TryParseTenantId(candidateValue, out parsedTenantId))
                {
                    continue;
                }

                if (EqualityComparer<TKey>.Default.Equals(parsedTenantId, tenantId))
                {
                    return true;
                }
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }

    private static bool TryParseTenantId<TKey>(string value, out TKey? tenantId)
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        if (TKey.TryParse(value, null, out TKey? parsedTenantId))
        {
            tenantId = parsedTenantId;
            return true;
        }

        tenantId = default;
        return false;
    }

    private static bool TryGetCandidateValue(JsonElement element, out string? value)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                value = element.GetString();
                return !string.IsNullOrWhiteSpace(value);
            case JsonValueKind.Number:
                value = element.GetRawText();
                return true;
            default:
                value = null;
                return false;
        }
    }
}
