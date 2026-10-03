# `TenantryAspNetCoreTenantBuilderExtensions` class

Namespace: `Microsoft.Extensions.DependencyInjection` · Package: `Tenantry.AspNetCore` · [API reference](README.md)

Tenantry's ASP.NET Core features on [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): how a request is resolved to a tenant, whether endpoints need one, and who may use it. `app.UseTenantry()` applies them to requests.

```csharp
public static class TenantryAspNetCoreTenantBuilderExtensions
```

## Methods

### `ConfigureResolution<TKey>(ITenantBuilder<TKey>, Action<TenantResolutionOptions<TKey>>)`

Configures how requests are treated: whether they need a tenant, the status code of each rejection, and the events raised when a request's tenant is made current or a request is rejected.

```csharp
public static ITenantBuilder<TKey> ConfigureResolution<TKey>(this ITenantBuilder<TKey> builder, Action<TenantResolutionOptions<TKey>> configure) where TKey : IEquatable<TKey>, IParsable<TKey>
```

Type parameters:

- `TKey`: The tenant identifier type.

Parameters:

- `builder` [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The tenant builder.
- `configure` `Action<TenantResolutionOptions<TKey>>`: Sets the options.

Returns: [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The same `builder` for chaining.

### `IsolateOutputCache<TKey>(ITenantBuilder<TKey>)`

Keeps ASP.NET Core's output cache per tenant: a response cached while a tenant is current varies by the tenant, so it is served only to that tenant, and invalidating the tenant ([`ITenantStoreCache<TKey>.Invalidate`](tenantry-itenantstorecache.md)) evicts it. A response for a request without a tenant (an endpoint that allows one to be missing) is cached apart from every tenant's.

```csharp
public static ITenantBuilder<TKey> IsolateOutputCache<TKey>(this ITenantBuilder<TKey> builder) where TKey : IEquatable<TKey>, IParsable<TKey>
```

Type parameters:

- `TKey`: The tenant identifier type.

Parameters:

- `builder` [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The tenant builder.

Returns: [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The same `builder` for chaining.

Add the output cache after Tenantry in the pipeline (`app.UseTenantry()`, then `app.UseOutputCache()`), so the tenant is known when the cache runs. A response for a request `app.UseTenantry()` did not handle (the other order, or a branch without it) is not cached, and a warning says so once. Endpoints still opt in to output caching themselves (`CacheOutput()`, `[OutputCache]`).

```csharp
builder.Services.AddOutputCache();
builder.Services.AddTenantry<Guid>(tenant => tenant
    .ResolveFromSubdomain()
    .UseStore<AppTenantStore>()
    .IsolateOutputCache());

app.UseTenantry(); app.UseOutputCache(); ```

### `RequireTenantByDefault<TKey>(ITenantBuilder<TKey>)`

Requires a tenant on every endpoint that does not allow a missing one with `AllowMissingTenant()`.

```csharp
public static ITenantBuilder<TKey> RequireTenantByDefault<TKey>(this ITenantBuilder<TKey> builder) where TKey : IEquatable<TKey>, IParsable<TKey>
```

Type parameters:

- `TKey`: The tenant identifier type.

Parameters:

- `builder` [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The tenant builder.

Returns: [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The same `builder` for chaining.

### `ResolveFromClaim<TKey>(ITenantBuilder<TKey>, string)`

Resolves the tenant from a claim on the current request principal.

```csharp
public static ITenantBuilder<TKey> ResolveFromClaim<TKey>(this ITenantBuilder<TKey> builder, string claimType = "tenant_id") where TKey : IEquatable<TKey>, IParsable<TKey>
```

Type parameters:

- `TKey`: The tenant identifier type.

Parameters:

- `builder` [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The tenant builder.
- `claimType` `string`: The type of the claim that carries the tenant identifier.

Returns: [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The same `builder` for chaining.

### `ResolveFromHeader<TKey>(ITenantBuilder<TKey>, string)`

Resolves the tenant from the specified HTTP request header.

```csharp
public static ITenantBuilder<TKey> ResolveFromHeader<TKey>(this ITenantBuilder<TKey> builder, string headerName) where TKey : IEquatable<TKey>, IParsable<TKey>
```

Type parameters:

- `TKey`: The tenant identifier type.

Parameters:

- `builder` [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The tenant builder.
- `headerName` `string`: The name of the header that carries the tenant identifier, such as `X-Tenant-Id`.

Returns: [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The same `builder` for chaining.

### `ResolveFromHost<TKey>(ITenantBuilder<TKey>, Action<HostTenantResolverOptions>?)`

Resolves the tenant from the request's host name, such as `app.acme.com`, for tenants with domains of their own, as [`HostTenantResolver`](tenantry-aspnetcore-hosttenantresolver.md) describes. Your tenant store's [`ITenantStore<TKey>.FindByIdentifierAsync`](tenantry-itenantstore.md) maps the host name to a tenant.

```csharp
public static ITenantBuilder<TKey> ResolveFromHost<TKey>(this ITenantBuilder<TKey> builder, Action<HostTenantResolverOptions>? configure = null) where TKey : IEquatable<TKey>, IParsable<TKey>
```

Type parameters:

- `TKey`: The tenant identifier type.

Parameters:

- `builder` [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The tenant builder.
- `configure` `Action<HostTenantResolverOptions>`: Sets the domains whose hosts are not tenants (`localhost` by default), or [null](https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/null) for the defaults.

Returns: [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The same `builder` for chaining.

It resolves every host that is not an IP address or excluded, so resolvers added after it never run for those hosts: add it last. Exclude your own domain, so its hosts do not ask the store for a tenant on every request.

```csharp
tenant
    .ResolveFromSubdomain(o => o.BaseDomains.Add("example.com"))   // acme.example.com
    .ResolveFromHost(o => o.ExcludedDomains.Add("example.com"));   // app.acme.com
```

### `ResolveFromPropagationHeader<TKey>(ITenantBuilder<TKey>)`

Resolves the tenant another service sent with its request: the tenant id in the [`TenantPropagation.HeaderName`](tenantry-tenantpropagation.md) header, which Tenantry.Http's `UseTenantry()` adds to an HttpClient's or gRPC client's requests. The value is read as a tenant id ([`TenantIds.TryParse<TKey>`](tenantry-tenantids.md)) and looked up with the store's [`ITenantStore<TKey>.GetTenantAsync`](tenantry-itenantstore.md), not its [`ITenantStore<TKey>.FindByIdentifierAsync`](tenantry-itenantstore.md); a value that is not a tenant id finds no tenant.

```csharp
public static ITenantBuilder<TKey> ResolveFromPropagationHeader<TKey>(this ITenantBuilder<TKey> builder) where TKey : IEquatable<TKey>, IParsable<TKey>
```

Type parameters:

- `TKey`: The tenant identifier type.

Parameters:

- `builder` [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The tenant builder.

Returns: [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The same `builder` for chaining.

A header is a claim, not proof: any caller that reaches the service can set it. Accept it only from callers you authenticate (a token, mutual TLS), and check that the caller may act for the tenant with `ValidateTenantAccess`. Resolvers run in the order they are added, and the first that finds a value wins.

```csharp
builder.Services.AddTenantry<Guid>(tenant => tenant
    .ResolveFromPropagationHeader()
    .UseStore<AppTenantStore>()
    .ValidateTenantAccess<CallingServiceValidator>());
```

### `ResolveFromQueryString<TKey>(ITenantBuilder<TKey>, string)`

Resolves the tenant from a query string parameter.

```csharp
public static ITenantBuilder<TKey> ResolveFromQueryString<TKey>(this ITenantBuilder<TKey> builder, string parameterName = "tenantId") where TKey : IEquatable<TKey>, IParsable<TKey>
```

Type parameters:

- `TKey`: The tenant identifier type.

Parameters:

- `builder` [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The tenant builder.
- `parameterName` `string`: The name of the query string parameter that carries the tenant identifier.

Returns: [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The same `builder` for chaining.

**For local development and testing only — do not use in production.** Query string parameters are routinely logged by servers, proxies, and analytics, and are trivially spoofable, so they are not a safe tenant-resolution mechanism for production traffic.

### `ResolveFromRouteValue<TKey>(ITenantBuilder<TKey>, string)`

Resolves the tenant from a route value.

```csharp
public static ITenantBuilder<TKey> ResolveFromRouteValue<TKey>(this ITenantBuilder<TKey> builder, string routeValueKey = "tenant") where TKey : IEquatable<TKey>, IParsable<TKey>
```

Type parameters:

- `TKey`: The tenant identifier type.

Parameters:

- `builder` [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The tenant builder.
- `routeValueKey` `string`: The name of the route value that carries the tenant identifier, as in `/api/{tenant}/orders`.

Returns: [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The same `builder` for chaining.

### `ResolveFromSubdomain<TKey>(ITenantBuilder<TKey>, Action<SubdomainTenantResolverOptions>?)`

Resolves the tenant from the subdomain of the request host, as [`SubdomainTenantResolver`](tenantry-aspnetcore-subdomaintenantresolver.md) describes.

```csharp
public static ITenantBuilder<TKey> ResolveFromSubdomain<TKey>(this ITenantBuilder<TKey> builder, Action<SubdomainTenantResolverOptions>? configure = null) where TKey : IEquatable<TKey>, IParsable<TKey>
```

Type parameters:

- `TKey`: The tenant identifier type.

Parameters:

- `builder` [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The tenant builder.
- `configure` `Action<SubdomainTenantResolverOptions>`: Sets the base domains and the subdomains that are not tenants (`www` by default), or [null](https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/null) for the defaults.

Returns: [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The same `builder` for chaining.

### `UseResolver<TResolver>(ITenantBuilder)`

Registers a custom [`ITenantResolver`](tenantry-aspnetcore-itenantresolver.md) implementation, created through dependency injection in each request's scope, so it can depend on scoped services such as a `DbContext`.

```csharp
public static ITenantBuilder UseResolver<TResolver>(this ITenantBuilder builder) where TResolver : class, ITenantResolver
```

Type parameters:

- `TResolver`: The resolver type.

Parameters:

- `builder` [`ITenantBuilder`](tenantry-itenantbuilder.md): The tenant builder.

Returns: [`ITenantBuilder`](tenantry-itenantbuilder.md): The same `builder`, without its key type: call methods that need it first.

### `UseResolver<TKey>(ITenantBuilder<TKey>, Func<IServiceProvider, ITenantResolver>)`

Registers a custom [`ITenantResolver`](tenantry-aspnetcore-itenantresolver.md) created by a factory, as a singleton.

```csharp
public static ITenantBuilder<TKey> UseResolver<TKey>(this ITenantBuilder<TKey> builder, Func<IServiceProvider, ITenantResolver> factory) where TKey : IEquatable<TKey>, IParsable<TKey>
```

Type parameters:

- `TKey`: The tenant identifier type.

Parameters:

- `builder` [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The tenant builder.
- `factory` `Func<IServiceProvider, ITenantResolver>`: Creates the resolver from the application's services.

Returns: [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The same `builder` for chaining.

### `UseResolver<TKey>(ITenantBuilder<TKey>, ITenantResolver)`

Registers a custom [`ITenantResolver`](tenantry-aspnetcore-itenantresolver.md) instance.

```csharp
public static ITenantBuilder<TKey> UseResolver<TKey>(this ITenantBuilder<TKey> builder, ITenantResolver resolver) where TKey : IEquatable<TKey>, IParsable<TKey>
```

Type parameters:

- `TKey`: The tenant identifier type.

Parameters:

- `builder` [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The tenant builder.
- `resolver` [`ITenantResolver`](tenantry-aspnetcore-itenantresolver.md): The resolver to use for every request.

Returns: [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The same `builder` for chaining.

### `ValidateTenantAccessByClaim<TKey>(ITenantBuilder<TKey>, string)`

Validates tenant access by matching the resolved tenant against claims on the current request principal. Supports repeated claims with single tenant ids and JSON array claim values.

```csharp
public static ITenantBuilder<TKey> ValidateTenantAccessByClaim<TKey>(this ITenantBuilder<TKey> builder, string claimType) where TKey : IEquatable<TKey>, IParsable<TKey>
```

Type parameters:

- `TKey`: The tenant identifier type.

Parameters:

- `builder` [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The tenant builder.
- `claimType` `string`: The type of the claims that list the tenant identifiers the principal may use. A request for any other tenant is refused.

Returns: [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The same `builder` for chaining.

### `ValidateTenantAccess<TValidator>(ITenantBuilder)`

Adds an access validator of type `TValidator`, created in each request's scope, so it can depend on scoped services such as a `DbContext`. Every validator must allow a request before its tenant is made current.

```csharp
public static ITenantBuilder ValidateTenantAccess<TValidator>(this ITenantBuilder builder) where TValidator : class
```

Type parameters:

- `TValidator`: The validator type, which implements [`ITenantAccessValidator<TKey>`](tenantry-aspnetcore-itenantaccessvalidator.md) for the application's tenant key type.

Parameters:

- `builder` [`ITenantBuilder`](tenantry-itenantbuilder.md): The tenant builder.

Returns: [`ITenantBuilder`](tenantry-itenantbuilder.md): The same `builder`, without its key type: call methods that need it first.

Exceptions:

- `InvalidOperationException`: `TValidator` does not implement [`ITenantAccessValidator<TKey>`](tenantry-aspnetcore-itenantaccessvalidator.md) for the builder's key type.

### `ValidateTenantAccess<TKey>(ITenantBuilder<TKey>, Func<HttpContext, ITenantDescriptor<TKey>, bool>)`

Adds a synchronous tenant access validator. Every validator must allow a request before its tenant is made current.

```csharp
public static ITenantBuilder<TKey> ValidateTenantAccess<TKey>(this ITenantBuilder<TKey> builder, Func<HttpContext, ITenantDescriptor<TKey>, bool> validator) where TKey : IEquatable<TKey>, IParsable<TKey>
```

Type parameters:

- `TKey`: The tenant identifier type.

Parameters:

- `builder` [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The tenant builder.
- `validator` `Func<HttpContext, ITenantDescriptor<TKey>, bool>`: Returns [true](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/bool) when the request may use the resolved tenant; otherwise an endpoint that needs a tenant refuses the request with [`TenantResolutionOptions<TKey>.AccessDeniedStatusCode`](tenantry-aspnetcore-tenantresolutionoptions.md), and any other runs without one.

Returns: [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The same `builder` for chaining.

### `ValidateTenantAccess<TKey>(ITenantBuilder<TKey>, Func<HttpContext, ITenantDescriptor<TKey>, CancellationToken, ValueTask<bool>>)`

Adds an asynchronous tenant access validator. Every validator must allow a request before its tenant is made current.

```csharp
public static ITenantBuilder<TKey> ValidateTenantAccess<TKey>(this ITenantBuilder<TKey> builder, Func<HttpContext, ITenantDescriptor<TKey>, CancellationToken, ValueTask<bool>> validator) where TKey : IEquatable<TKey>, IParsable<TKey>
```

Type parameters:

- `TKey`: The tenant identifier type.

Parameters:

- `builder` [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The tenant builder.
- `validator` `Func<HttpContext, ITenantDescriptor<TKey>, CancellationToken, ValueTask<bool>>`: Returns [true](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/bool) when the request may use the resolved tenant; otherwise an endpoint that needs a tenant refuses the request with [`TenantResolutionOptions<TKey>.AccessDeniedStatusCode`](tenantry-aspnetcore-tenantresolutionoptions.md), and any other runs without one. It receives the request's cancellation token.

Returns: [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The same `builder` for chaining.
