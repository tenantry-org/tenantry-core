# `TenantryApplicationBuilderExtensions` class

Namespace: `Microsoft.AspNetCore.Builder` · Package: `Tenantry.AspNetCore` · [API reference](README.md)

Adds Tenantry's tenant resolution to the request pipeline.

```csharp
public static class TenantryApplicationBuilderExtensions
```

## Methods

### `UseTenantResolution(IApplicationBuilder)`

Resolves the request's tenant before authentication and makes it current, so authentication handlers read the tenant's options (`ConfigurePerTenant<JwtBearerOptions>(scheme, …)` in Tenantry.Options). Call it before `app.UseAuthentication()`, and [`TenantryApplicationBuilderExtensions.UseTenantry`](microsoft-aspnetcore-builder-tenantryapplicationbuilderextensions.md) after it: that runs the access validators, and the claim resolvers if nothing else named a tenant, then rejects or continues as it does alone.

```csharp
public static IApplicationBuilder UseTenantResolution(this IApplicationBuilder app)
```

Parameters:

- `app` `IApplicationBuilder`: The application's request pipeline.

Returns: `IApplicationBuilder`: The same `app` for chaining.

Exceptions:

- `InvalidOperationException`: Tenantry is not registered with a way to resolve requests, or no tenant store is registered. The application also fails to start if [`TenantryApplicationBuilderExtensions.UseTenantry`](microsoft-aspnetcore-builder-tenantryapplicationbuilderextensions.md) is not in the pipeline.

Between the two, the tenant is current but not yet checked against the user, so put only `app.UseAuthentication()` between them. An endpoint whose request did not pass through [`TenantryApplicationBuilderExtensions.UseTenantry`](microsoft-aspnetcore-builder-tenantryapplicationbuilderextensions.md) after this does not run: it gets `500` and log event 1011. Resolve from the host, subdomain, route or a header here; claim resolvers wait for [`TenantryApplicationBuilderExtensions.UseTenantry`](microsoft-aspnetcore-builder-tenantryapplicationbuilderextensions.md).

```csharp
app.UseTenantResolution();
app.UseAuthentication();
app.UseTenantry();
app.UseAuthorization();
```

### `UseTenantry(IApplicationBuilder)`

Adds the tenant resolution middleware to the pipeline. Must be called after authentication middleware (`app.UseAuthentication()`) if using claim-based resolution, and before any middleware that requires a resolved tenant (e.g. authorisation, controllers). When using `RequireTenant()` or `AllowMissingTenant()` endpoint metadata, ensure routing has executed before this middleware. `WebApplication` handles this automatically for minimal APIs and controllers. After [`TenantryApplicationBuilderExtensions.UseTenantResolution`](microsoft-aspnetcore-builder-tenantryapplicationbuilderextensions.md), it completes what that started: it runs the access validators on the tenant found before authentication, and the claim resolvers if nothing else named one.

```csharp
public static IApplicationBuilder UseTenantry(this IApplicationBuilder app)
```

Parameters:

- `app` `IApplicationBuilder`: The application's request pipeline.

Returns: `IApplicationBuilder`: The same `app` for chaining.

Exceptions:

- `InvalidOperationException`: Tenantry is not registered with a way to resolve requests (a `ResolveFrom…` or `UseResolver` method in `AddTenantry`), or no tenant store is registered.
