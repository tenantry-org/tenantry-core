using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Tenantry.AspNetCore.Extensions;
using Tenantry.AspNetCore.Resolution;
using Tenantry.Core;
using Tenantry.Core.Internal;

namespace Tenantry.AspNetCore.Internal;

/// <summary>
/// Fluent builder for configuring multi-tenancy services.
/// Obtained from <see cref="ServiceCollectionExtensions.AddTenantry{TKey}"/>.
/// </summary>
/// <typeparam name="TKey">
/// The tenant identifier type. Must implement <see cref="IEquatable{T}"/> and <see cref="IParsable{T}"/>.
/// </typeparam>
internal sealed class AspNetCoreTenantBuilder<TKey>(IServiceCollection services) : 
    TenantBuilder<TKey>(services),
    IAspNetCoreTenantBuilder<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    /// <summary>
    /// Resolves the tenant from the specified HTTP request header.
    /// </summary>
    /// <param name="headerName">The header name, e.g. <c>"X-Tenant-Id"</c>.</param>
    public IAspNetCoreTenantBuilder<TKey> ResolveFromHeader(string headerName)
    {
        Services.AddSingleton<ITenantResolver>(new HeaderTenantResolver(headerName));
        return this;
    }

    /// <summary>
    /// Resolves the tenant from the first subdomain of the request host.
    /// </summary>
    public IAspNetCoreTenantBuilder<TKey> ResolveFromSubdomain()
    {
        Services.AddSingleton<ITenantResolver, SubdomainTenantResolver>();
        return this;
    }

    /// <summary>
    /// Resolves the tenant from a route value.
    /// </summary>
    /// <param name="routeValueKey">The route value key, e.g. <c>"tenant"</c> for <c>/api/{tenant}/resource</c>.</param>
    public IAspNetCoreTenantBuilder<TKey> ResolveFromRouteValue(string routeValueKey = "tenant")
    {
        Services.AddSingleton<ITenantResolver>(new RouteValueTenantResolver(routeValueKey));
        return this;
    }

    /// <summary>
    /// Resolves the tenant from a claim on the current request principal.
    /// </summary>
    /// <param name="claimType">The claim type, e.g. <c>"tenant_id"</c>.</param>
    public IAspNetCoreTenantBuilder<TKey> ResolveFromClaim(string claimType = "tenant_id")
    {
        Services.AddSingleton<ITenantResolver>(new ClaimTenantResolver(claimType));
        return this;
    }

    /// <summary>
    /// Resolves the tenant from a query string parameter.
    /// For development and testing only — do not use in production.
    /// </summary>
    /// <param name="parameterName">The query string parameter, e.g. <c>"tenantId"</c>.</param>
    public IAspNetCoreTenantBuilder<TKey> ResolveFromQueryString(string parameterName = "tenantId")
    {
        Services.AddSingleton<ITenantResolver>(new QueryStringTenantResolver(parameterName));
        return this;
    }

    /// <summary>
    /// Registers a custom <see cref="ITenantResolver"/> implementation.
    /// </summary>
    public IAspNetCoreTenantBuilder<TKey> UseResolver<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TResolver>()
        where TResolver : class, ITenantResolver
    {
        Services.AddSingleton<ITenantResolver, TResolver>();
        return this;
    }
    
    /// <summary>
    /// Registers a custom <see cref="ITenantResolver"/> implementation with a concrete instance.
    /// </summary>
    public IAspNetCoreTenantBuilder<TKey> UseResolver(ITenantResolver resolver)
    {
        Services.AddSingleton(resolver);
        return this;
    }
    
    /// <summary>
    /// Registers a custom <see cref="ITenantResolver"/> implementation with a factory method.
    /// </summary>
    public IAspNetCoreTenantBuilder<TKey> UseResolver(
        Func<IServiceProvider, ITenantResolver> factory)
    {
        Services.AddSingleton(factory);
        return this;
    }

    /// <summary>
    /// Requires tenant resolution by default for requests that pass through Tenantry middleware.
    /// Endpoints can override this default with <c>AllowMissingTenant()</c>.
    /// </summary>
    public IAspNetCoreTenantBuilder<TKey> RequireTenantByDefault()
    {
        GetResolutionOptions().RequireTenantByDefault = true;
        return this;
    }

    /// <summary>
    /// Validates that the current request is allowed to access the resolved tenant
    /// by matching the tenant against one or more claims of the specified type on the current request principal.
    /// Supports repeated claims with single tenant IDs and JSON array claim values.
    /// </summary>
    /// <param name="claimType">The claim type containing one or more allowed tenant identifiers.</param>
    public IAspNetCoreTenantBuilder<TKey> ValidateTenantAccessByClaim(string claimType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(claimType);

        return ValidateTenantAccess((httpContext, tenant, _) =>
            ClaimTenantAccessValidator.ValidateAsync(httpContext, tenant, claimType));
    }

    /// <summary>
    /// Adds a composite tenant access validator that succeeds when any configured validation group passes.
    /// Validators within a group are combined with logical AND.
    /// </summary>
    public IAspNetCoreTenantBuilder<TKey> ValidateTenantAccessAny(
        params Action<ITenantAccessValidationGroupBuilder<TKey>>[] groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        
        if (groups.Length == 0)
        {
            throw new ArgumentException("At least one validation group must be provided.", nameof(groups));
        }

        List<IReadOnlyList<Func<HttpContext, ITenantDescriptor<TKey>, CancellationToken, ValueTask<bool>>>> builtGroups = [];

        foreach (var configureGroup in groups)
        {
            ArgumentNullException.ThrowIfNull(configureGroup);

            TenantAccessValidationGroupBuilder<TKey> groupBuilder = new();
            configureGroup(groupBuilder);
            builtGroups.Add(groupBuilder.Build());
        }

        return ValidateTenantAccess(async (httpContext, tenant, cancellationToken) =>
        {
            foreach (var group in builtGroups)
            {
                var passed = true;

                foreach (var validator in group)
                {
                    if (await validator(httpContext, tenant, cancellationToken))
                    {
                        continue;
                    }
                    
                    passed = false;
                    break;
                }

                if (passed)
                {
                    return true;
                }
            }

            return false;
        });
    }

    /// <summary>
    /// Validates that the current request is allowed to access the resolved tenant
    /// before Tenantry sets the tenant context.
    /// </summary>
    /// <param name="validator">
    /// A synchronous validator that returns <see langword="true"/> when the current user
    /// may access the resolved tenant, otherwise <see langword="false"/>.
    /// </param>
    public IAspNetCoreTenantBuilder<TKey> ValidateTenantAccess(
        Func<HttpContext, ITenantDescriptor<TKey>, bool> validator)
    {
        ArgumentNullException.ThrowIfNull(validator);

        return ValidateTenantAccess((httpContext, tenant, _) =>
            ValueTask.FromResult(validator(httpContext, tenant)));
    }

    /// <summary>
    /// Validates that the current request is allowed to access the resolved tenant
    /// before Tenantry sets the tenant context.
    /// </summary>
    /// <param name="validator">
    /// An asynchronous validator that returns <see langword="true"/> when the current user
    /// may access the resolved tenant, otherwise <see langword="false"/>.
    /// </param>
    public IAspNetCoreTenantBuilder<TKey> ValidateTenantAccess(
        Func<HttpContext, ITenantDescriptor<TKey>, CancellationToken, ValueTask<bool>> validator)
    {
        ArgumentNullException.ThrowIfNull(validator);

        GetResolutionOptions().AccessValidators.Add(validator);
        return this;
    }

    private TenantResolutionOptions<TKey> GetResolutionOptions() =>
        Services
            .LastOrDefault(sd => sd.ServiceType == typeof(TenantResolutionOptions<TKey>))
            ?.ImplementationInstance as TenantResolutionOptions<TKey>
        ?? throw new InvalidOperationException(
            $"Tenantry internal error: {nameof(TenantResolutionOptions<>)} was not registered.");
}
