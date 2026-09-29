# `IAspNetCoreTenantBuilder<TKey>` interface

Namespace: `Tenantry.AspNetCore` · Package: `Tenantry.AspNetCore` · [API reference](README.md)

Fluent builder for configuring multi-tenancy services. Obtained from [`ServiceCollectionExtensions.AddTenantry<TKey>`](tenantry-aspnetcore-extensions-servicecollectionextensions.md).

```csharp
public interface IAspNetCoreTenantBuilder<TKey> : ITenantBuilder<TKey> where TKey : IEquatable<TKey>, IParsable<TKey>
```

## Type parameters

- `TKey`: The tenant identifier type. Must implement `IEquatable<T>` and `IParsable<TSelf>`.

## Methods

### `RequireTenantByDefault()`

Requires tenant resolution by default for requests that pass through Tenantry middleware.

```csharp
IAspNetCoreTenantBuilder<TKey> RequireTenantByDefault()
```

Returns: [`IAspNetCoreTenantBuilder<TKey>`](tenantry-aspnetcore-iaspnetcoretenantbuilder.md)

### `ResolveFromClaim(string)`

Resolves the tenant from a claim on the current request principal.

```csharp
IAspNetCoreTenantBuilder<TKey> ResolveFromClaim(string claimType = "tenant_id")
```

Parameters:

- `claimType` `string`: The type of the claim that carries the tenant identifier.

Returns: [`IAspNetCoreTenantBuilder<TKey>`](tenantry-aspnetcore-iaspnetcoretenantbuilder.md)

### `ResolveFromHeader(string)`

Resolves the tenant from the specified HTTP request header.

```csharp
IAspNetCoreTenantBuilder<TKey> ResolveFromHeader(string headerName)
```

Parameters:

- `headerName` `string`: The name of the header that carries the tenant identifier, such as `X-Tenant-Id`.

Returns: [`IAspNetCoreTenantBuilder<TKey>`](tenantry-aspnetcore-iaspnetcoretenantbuilder.md)

### `ResolveFromQueryString(string)`

Resolves the tenant from a query string parameter.

```csharp
IAspNetCoreTenantBuilder<TKey> ResolveFromQueryString(string parameterName = "tenantId")
```

Parameters:

- `parameterName` `string`: The name of the query string parameter that carries the tenant identifier.

Returns: [`IAspNetCoreTenantBuilder<TKey>`](tenantry-aspnetcore-iaspnetcoretenantbuilder.md)

**For local development and testing only — do not use in production.** Query string parameters are routinely logged by servers, proxies, and analytics, and are trivially spoofable, so they are not a safe tenant-resolution mechanism for production traffic.

### `ResolveFromRouteValue(string)`

Resolves the tenant from a route value.

```csharp
IAspNetCoreTenantBuilder<TKey> ResolveFromRouteValue(string routeValueKey = "tenant")
```

Parameters:

- `routeValueKey` `string`: The name of the route value that carries the tenant identifier, as in `/api/{tenant}/orders`.

Returns: [`IAspNetCoreTenantBuilder<TKey>`](tenantry-aspnetcore-iaspnetcoretenantbuilder.md)

### `ResolveFromSubdomain()`

Resolves the tenant from the first subdomain of the request host.

```csharp
IAspNetCoreTenantBuilder<TKey> ResolveFromSubdomain()
```

Returns: [`IAspNetCoreTenantBuilder<TKey>`](tenantry-aspnetcore-iaspnetcoretenantbuilder.md)

### `UseResolver(Func<IServiceProvider, ITenantResolver>)`

Registers a custom [`ITenantResolver`](tenantry-aspnetcore-resolution-itenantresolver.md) implementation with a factory method.

```csharp
IAspNetCoreTenantBuilder<TKey> UseResolver(Func<IServiceProvider, ITenantResolver> factory)
```

Parameters:

- `factory` `Func<IServiceProvider, ITenantResolver>`: Creates the resolver from the application's services.

Returns: [`IAspNetCoreTenantBuilder<TKey>`](tenantry-aspnetcore-iaspnetcoretenantbuilder.md)

### `UseResolver(ITenantResolver)`

Registers a custom [`ITenantResolver`](tenantry-aspnetcore-resolution-itenantresolver.md) implementation with a concrete instance.

```csharp
IAspNetCoreTenantBuilder<TKey> UseResolver(ITenantResolver resolver)
```

Parameters:

- `resolver` [`ITenantResolver`](tenantry-aspnetcore-resolution-itenantresolver.md): The resolver to use for every request.

Returns: [`IAspNetCoreTenantBuilder<TKey>`](tenantry-aspnetcore-iaspnetcoretenantbuilder.md)

### `UseResolver<TResolver>()`

Registers a custom [`ITenantResolver`](tenantry-aspnetcore-resolution-itenantresolver.md) implementation.

```csharp
IAspNetCoreTenantBuilder<TKey> UseResolver<TResolver>() where TResolver : class, ITenantResolver
```

Type parameters:

- `TResolver`: The resolver type, created through dependency injection.

Returns: [`IAspNetCoreTenantBuilder<TKey>`](tenantry-aspnetcore-iaspnetcoretenantbuilder.md)

### `ValidateTenantAccess(Func<HttpContext, ITenantDescriptor<TKey>, bool>)`

Adds a synchronous tenant access validator.

```csharp
IAspNetCoreTenantBuilder<TKey> ValidateTenantAccess(Func<HttpContext, ITenantDescriptor<TKey>, bool> validator)
```

Parameters:

- `validator` `Func<HttpContext, ITenantDescriptor<TKey>, bool>`: Returns [true](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/bool) when the request may use the resolved tenant; otherwise the request is refused with `403 Forbidden`.

Returns: [`IAspNetCoreTenantBuilder<TKey>`](tenantry-aspnetcore-iaspnetcoretenantbuilder.md)

### `ValidateTenantAccess(Func<HttpContext, ITenantDescriptor<TKey>, CancellationToken, ValueTask<bool>>)`

Adds an asynchronous tenant access validator.

```csharp
IAspNetCoreTenantBuilder<TKey> ValidateTenantAccess(Func<HttpContext, ITenantDescriptor<TKey>, CancellationToken, ValueTask<bool>> validator)
```

Parameters:

- `validator` `Func<HttpContext, ITenantDescriptor<TKey>, CancellationToken, ValueTask<bool>>`: Returns [true](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/bool) when the request may use the resolved tenant; otherwise the request is refused with `403 Forbidden`. It receives the request's cancellation token.

Returns: [`IAspNetCoreTenantBuilder<TKey>`](tenantry-aspnetcore-iaspnetcoretenantbuilder.md)

### `ValidateTenantAccessByClaim(string)`

Validates tenant access by matching the resolved tenant against claims on the current request principal.

```csharp
IAspNetCoreTenantBuilder<TKey> ValidateTenantAccessByClaim(string claimType)
```

Parameters:

- `claimType` `string`: The type of the claims that list the tenant identifiers the principal may use. A request for any other tenant is refused.

Returns: [`IAspNetCoreTenantBuilder<TKey>`](tenantry-aspnetcore-iaspnetcoretenantbuilder.md)
