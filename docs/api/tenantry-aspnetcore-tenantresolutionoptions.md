# `TenantResolutionOptions<TKey>` class

Namespace: `Tenantry.AspNetCore` · Package: `Tenantry.AspNetCore` · [API reference](README.md)

How `app.UseTenantry()` treats requests: whether they need a tenant, the status code of each rejection, and the events it raises. Configure it with `tenant.ConfigureResolution(o => …)` or `tenant.RequireTenantByDefault()`.

A rejected request gets the status code and, when an `IProblemDetailsService` is registered (`builder.Services.AddProblemDetails()`), a problem details body; otherwise an empty body. The body never repeats the identifier the request sent. [`TenantResolutionOptions<TKey>.OnRejected`](tenantry-aspnetcore-tenantresolutionoptions.md) can write another response.

Only an endpoint that needs a tenant rejects a request. On any other endpoint, a request whose identifier names no tenant, or names a tenant it may not use, continues without a tenant.

```csharp
public sealed class TenantResolutionOptions<TKey> where TKey : IEquatable<TKey>, IParsable<TKey>
```

## Type parameters

- `TKey`: The tenant identifier type.

## Properties

### `AccessDeniedStatusCode`

The status code when an access validator refuses the request's tenant. Default `403 Forbidden`.

```csharp
public int AccessDeniedStatusCode { get; set; }
```

Value: `int`

### `MissingTenantStatusCode`

The status code when the request does not identify a tenant. Default `400 Bad Request`.

```csharp
public int MissingTenantStatusCode { get; set; }
```

Value: `int`

### `OnRejected`

Called when an endpoint that needs a tenant rejects a request, before Tenantry writes its response. Call [`TenantRejectedContext<TKey>.HandleResponse`](tenantry-aspnetcore-tenantrejectedcontext.md) after writing your own response (a redirect, or an error page), or change [`TenantRejectedContext<TKey>.StatusCode`](tenantry-aspnetcore-tenantrejectedcontext.md) and leave the response to Tenantry.

```csharp
public Func<TenantRejectedContext<TKey>, Task>? OnRejected { get; set; }
```

Value: `Func<TenantRejectedContext<TKey>, Task>`

### `OnResolved`

Called when a request's tenant is made current, before the rest of the pipeline runs: to add the tenant to your own telemetry, say. To refuse a tenant, use an access validator.

```csharp
public Func<TenantResolvedContext<TKey>, Task>? OnResolved { get; set; }
```

Value: `Func<TenantResolvedContext<TKey>, Task>`

### `RequireTenantByDefault`

Whether an endpoint without `RequireTenant()` or `AllowMissingTenant()` needs a tenant. Default [false](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/bool).

```csharp
public bool RequireTenantByDefault { get; set; }
```

Value: `bool`

### `TenantNotFoundStatusCode`

The status code when the request's identifier names no tenant: the tenant store's [`ITenantStore<TKey>.FindByIdentifierAsync`](tenantry-itenantstore.md) finds none. Default `404 Not Found`. When access validators are configured, [`TenantResolutionOptions<TKey>.AccessDeniedStatusCode`](tenantry-aspnetcore-tenantresolutionoptions.md) and its response are used instead, so a caller cannot tell a tenant that does not exist from one it may not use.

```csharp
public int TenantNotFoundStatusCode { get; set; }
```

Value: `int`
