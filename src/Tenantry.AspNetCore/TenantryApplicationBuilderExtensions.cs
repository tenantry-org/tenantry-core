using Microsoft.Extensions.DependencyInjection;
using Tenantry.AspNetCore.Internal;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Adds Tenantry's tenant resolution to the request pipeline.
/// </summary>
public static class TenantryApplicationBuilderExtensions
{
    /// <summary>
    /// Resolves each request's tenant, checks it, and makes it current for the rest of the request. A request to an
    /// endpoint that requires a tenant is rejected if it names none, or one that is unknown or refused.
    /// </summary>
    /// <param name="app">The application's request pipeline.</param>
    /// <returns>The same <paramref name="app"/> for chaining.</returns>
    /// <remarks>
    /// Call it after <c>app.UseAuthentication()</c>, because claim resolvers and access validators read the user.
    /// Call it after routing, because it reads <c>RequireTenant()</c> and <c>AllowMissingTenant()</c>
    /// (<see cref="WebApplication"/> adds routing first). Call it before anything that needs the tenant. After
    /// <see cref="UseTenantResolution"/>, it runs the access validators on the tenant found before authentication, and
    /// the claim resolvers if nothing else named a tenant; <c>app.UseAuthorization()</c> then comes after it.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Tenantry is not registered with a way to resolve requests (a <c>ResolveFrom…</c> or <c>UseResolver</c>
    /// method in <c>AddTenantry</c>), or no tenant store is registered, or <c>app.UseAuthorization()</c> is between
    /// <see cref="UseTenantResolution"/> and this call.
    /// </exception>
    public static IApplicationBuilder UseTenantry(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return Configurator(app).Use(app);
    }

    /// <summary>
    /// Resolves the request's tenant before authentication and makes it current, so authentication handlers read the
    /// tenant's options (<c>Configure&lt;JwtBearerOptions&gt;(scheme, …)</c> in Tenantry.Options' <c>ConfigurePerTenant</c>).
    /// </summary>
    /// <param name="app">The application's request pipeline.</param>
    /// <returns>The same <paramref name="app"/> for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Call it before <c>app.UseAuthentication()</c>, and <see cref="UseTenantry"/> after it, which runs the access
    /// validators, and the claim resolvers if nothing else named a tenant, then rejects or continues as it does alone.
    /// Between the two, the tenant is current but not yet checked against the user, so put only
    /// <c>app.UseAuthentication()</c> between them, and <c>app.UseAuthorization()</c> after <see cref="UseTenantry"/>,
    /// so no authorization policy sees a tenant the access validators have not checked. An endpoint whose request did
    /// not pass through <see cref="UseTenantry"/> after this does not run: it gets <c>500</c> and log event 1011. A
    /// request the authorization middleware ran for between the two gets <c>500</c> and log event 1013. If the access
    /// validators refuse the tenant for a signed-in user, the request is refused whatever its endpoint requires, every
    /// scheme that signs out locally (each cookie scheme) is signed out, and the response sets none of the cookies set
    /// after this call, since the user was authenticated with that tenant current:
    /// authentication events and claims transformations must not grant anything from the current tenant.
    /// </para>
    /// <para>
    /// Only the resolvers added before the first that needs the user (a claim resolver, or
    /// <c>ResolveFromPropagationHeader</c>) run here, in order. If they find nothing, <see cref="UseTenantry"/> runs
    /// every resolver, in order, after authentication, so a resolver added after a claim resolver never wins over the
    /// claim; authentication then used the default settings.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Tenantry is not registered with a way to resolve requests, or no tenant store is registered. The application
    /// also fails to start if <see cref="UseTenantry"/> is not in the pipeline, or if <c>app.UseAuthorization()</c> is
    /// between the two.
    /// </exception>
    /// <example>
    /// <code>
    /// app.UseTenantResolution();
    /// app.UseAuthentication();
    /// app.UseTenantry();
    /// app.UseAuthorization();
    /// </code>
    /// </example>
    public static IApplicationBuilder UseTenantResolution(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return Configurator(app).UseResolution(app);
    }

    private static ITenantResolutionMiddlewareConfigurator Configurator(IApplicationBuilder app) =>
        app.ApplicationServices.GetService<ITenantResolutionMiddlewareConfigurator>()
        ?? throw new InvalidOperationException(
            "app.UseTenantry() found no tenant resolution to run. Register Tenantry with a way to resolve requests " +
            "to tenants before building the application, for example builder.Services.AddTenantry<Guid>(tenant => " +
            "tenant.ResolveFromHeader(\"X-Tenant-Id\").UseStore<AppTenantStore>()), or another ResolveFrom... or " +
            "UseResolver method.");
}
