using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tenantry.Http.Internal;

/// <summary>
/// The current tenant's id as <see cref="TenantPropagation.HeaderName"/> carries it, for the handler, which has no
/// key type: <c>UseTenantry()</c> registers it for the application's, read from <see cref="ITenantKeyType"/>.
/// </summary>
internal interface ITenantHeaderSource
{
    /// <summary>The current tenant's id, formatted by <see cref="TenantIds.Format{TKey}"/>, or null with no tenant.</summary>
    /// <exception cref="InvalidOperationException">The key type does not parse the id back from that text.</exception>
    string? CurrentTenantId { get; }

    /// <summary>
    /// Whether <paramref name="value"/> names the current tenant as the receiving service reads it: trimmed, then
    /// parsed by <see cref="TenantIds.TryParse{TKey}"/>, so <c>3F2504E0-…</c> names the tenant <c>3f2504e0-…</c>.
    /// </summary>
    bool NamesCurrentTenant(string value);
}

internal sealed class TenantHeaderSource<TKey>(ITenantContext<TKey> tenantContext) : ITenantHeaderSource
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    public string? CurrentTenantId => tenantContext.CurrentTenant is { } tenant ? Format(tenant.TenantId) : null;

    public bool NamesCurrentTenant(string value) =>
        tenantContext.CurrentTenant is { } tenant &&
        TenantIds.TryParse<TKey>(value.Trim(), out var tenantId) &&
        tenantId.Equals(tenant.TenantId);

    // The receiving service parses the header with the key type's Parse. A key type whose ToString writes something
    // else, such as a record's "TenantId { Value = … }", would reach it as no tenant, so the call fails here instead.
    private static string Format(TKey tenantId)
    {
        var text = TenantIds.Format(tenantId);

        if (TKey.TryParse(text, CultureInfo.InvariantCulture, out var parsed) && parsed.Equals(tenantId))
            return text;

        throw new InvalidOperationException(
            $"The current tenant's id formats as '{text}', which {typeof(TKey).Name}.TryParse does not read back as " +
            $"the same id, so the service called could not find the tenant. Make {typeof(TKey).Name} implement " +
            "IFormattable, or override its ToString() to return the text its Parse reads.");
    }
}

internal static class TenantHeaderSource
{
    /// <summary>Registers the <see cref="ITenantHeaderSource"/> of the key type <c>AddTenantry</c> was called with.</summary>
    public static void Register(IServiceCollection services) =>
        services.TryAddSingleton(Create);

    /// <exception cref="InvalidOperationException">Tenantry is not registered.</exception>
    private static ITenantHeaderSource Create(IServiceProvider services)
    {
        var keyType = services.GetService<ITenantKeyType>() ?? throw new InvalidOperationException(
            "UseTenantry() sends the current tenant with a client's requests, but Tenantry is not registered. " +
            "Register it with builder.Services.AddTenantry<TKey>(...).");

        return keyType.Accept(new Factory(services));
    }

    private sealed class Factory(IServiceProvider services) : ITenantKeyTypeVisitor<ITenantHeaderSource>
    {
        public ITenantHeaderSource Visit<TKey>()
            where TKey : IEquatable<TKey>, IParsable<TKey> =>
            new TenantHeaderSource<TKey>(services.GetRequiredService<ITenantContext<TKey>>());
    }
}
