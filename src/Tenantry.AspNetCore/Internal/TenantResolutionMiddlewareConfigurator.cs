using Microsoft.AspNetCore.Builder;
using Tenantry.AspNetCore.Middleware;

namespace Tenantry.AspNetCore.Internal;

/// <summary>
/// Non-generic interface that enables <c>UseTenantry()</c> without repeating the TKey type parameter.
/// Registered during <c>AddTenantry&lt;TKey&gt;()</c> with a closed generic implementation.
/// </summary>
internal interface ITenantResolutionMiddlewareConfigurator
{
    IApplicationBuilder Use(IApplicationBuilder app);
}

internal sealed class TenantResolutionMiddlewareConfigurator<TKey> : ITenantResolutionMiddlewareConfigurator
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    public IApplicationBuilder Use(IApplicationBuilder app)
        => app.UseMiddleware<TenantResolutionMiddleware<TKey>>();
}
