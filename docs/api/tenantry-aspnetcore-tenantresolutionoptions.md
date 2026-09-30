# `TenantResolutionOptions` class

Namespace: `Tenantry.AspNetCore` · Package: `Tenantry.AspNetCore` · [API reference](README.md)

How `app.UseTenantry()` treats requests: whether they need a tenant, and the status code of each rejection. Configure it with `tenant.ConfigureResolution(o => …)` or `tenant.RequireTenantByDefault()`.

A rejected request gets the status code and, when an `IProblemDetailsService` is registered (`builder.Services.AddProblemDetails()`), a problem details body; otherwise an empty body. The body never repeats the identifier the request sent.

Only an endpoint that needs a tenant rejects a request. On any other endpoint, a request whose identifier is not valid, names no tenant, or names a tenant it may not use continues without a tenant.

```csharp
public sealed class TenantResolutionOptions
```

## Properties

### `AccessDeniedStatusCode`

The status code when an access validator refuses the request's tenant. Default `403 Forbidden`.

```csharp
public int AccessDeniedStatusCode { get; set; }
```

Value: `int`

### `InvalidTenantStatusCode`

The status code when the request's identifier cannot be a tenant id: it does not parse as the tenant key type, or it is the key type's default value. Default `400 Bad Request`.

```csharp
public int InvalidTenantStatusCode { get; set; }
```

Value: `int`

### `MissingTenantStatusCode`

The status code when the request does not identify a tenant. Default `400 Bad Request`.

```csharp
public int MissingTenantStatusCode { get; set; }
```

Value: `int`

### `RequireTenantByDefault`

Whether an endpoint without `RequireTenant()` or `AllowMissingTenant()` needs a tenant. Default [false](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/bool).

```csharp
public bool RequireTenantByDefault { get; set; }
```

Value: `bool`

### `TenantNotFoundStatusCode`

The status code when the tenant store has no tenant with the request's identifier. Default `404 Not Found`. When access validators are configured, [`TenantResolutionOptions.AccessDeniedStatusCode`](tenantry-aspnetcore-tenantresolutionoptions.md) and its response are used instead, so a caller cannot tell a tenant that does not exist from one it may not use.

```csharp
public int TenantNotFoundStatusCode { get; set; }
```

Value: `int`
