# `TenantryAspNetCoreTenantBuilderExtensions` class

Namespace: `Microsoft.Extensions.DependencyInjection` · Package: `Tenantry.AspNetCore` · [API reference](README.md)

Tenantry's ASP.NET Core features on [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): how a request is resolved to a tenant, whether endpoints need one, and who may use it. `app.UseTenantry()` applies them to requests.

```csharp
public static class TenantryAspNetCoreTenantBuilderExtensions
```

## Methods

### `ConfigureResolution<TKey>(ITenantBuilder<TKey>, Action<TenantResolutionOptions>)`

Configures how requests are treated: whether they need a tenant, and the status code of each rejection.

```csharp
public static ITenantBuilder<TKey> ConfigureResolution<TKey>(this ITenantBuilder<TKey> builder, Action<TenantResolutionOptions> configure) where TKey : IEquatable<TKey>, IParsable<TKey>
```

Type parameters:

- `TKey`: The tenant identifier type.

Parameters:

- `builder` [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The tenant builder.
- `configure` `Action<TenantResolutionOptions>`: Sets the options.

Returns: [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The same `builder` for chaining.

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
- `configure` `Action<SubdomainTenantResolverOptions>`: Sets the base domain and the subdomains that are not tenants (`www` by default), or [null](https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/null) for the defaults.

Returns: [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The same `builder` for chaining.

### `UseResolver<TResolver>(ITenantBuilder)`

Registers a custom [`ITenantResolver`](tenantry-aspnetcore-itenantresolver.md) implementation, created through dependency injection as a singleton.

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

### `ValidateTenantAccess<TKey>(ITenantBuilder<TKey>, Func<HttpContext, ITenantDescriptor<TKey>, bool>)`

Adds a synchronous tenant access validator. Every validator must allow a request before its tenant is made current.

```csharp
public static ITenantBuilder<TKey> ValidateTenantAccess<TKey>(this ITenantBuilder<TKey> builder, Func<HttpContext, ITenantDescriptor<TKey>, bool> validator) where TKey : IEquatable<TKey>, IParsable<TKey>
```

Type parameters:

- `TKey`: The tenant identifier type.

Parameters:

- `builder` [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The tenant builder.
- `validator` `Func<HttpContext, ITenantDescriptor<TKey>, bool>`: Returns [true](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/bool) when the request may use the resolved tenant; otherwise an endpoint that needs a tenant refuses the request with [`TenantResolutionOptions.AccessDeniedStatusCode`](tenantry-aspnetcore-tenantresolutionoptions.md), and any other runs without one.

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
- `validator` `Func<HttpContext, ITenantDescriptor<TKey>, CancellationToken, ValueTask<bool>>`: Returns [true](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/bool) when the request may use the resolved tenant; otherwise an endpoint that needs a tenant refuses the request with [`TenantResolutionOptions.AccessDeniedStatusCode`](tenantry-aspnetcore-tenantresolutionoptions.md), and any other runs without one. It receives the request's cancellation token.

Returns: [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The same `builder` for chaining.
