# `TenantryApplicationBuilderExtensions` class

Namespace: `Microsoft.AspNetCore.Builder` · Package: `Tenantry.AspNetCore` · [API reference](README.md)

Adds Tenantry's tenant resolution to the request pipeline.

```csharp
public static class TenantryApplicationBuilderExtensions
```

## Methods

### `UseTenantResolution(IApplicationBuilder)`

Resolves the request's tenant before authentication and makes it current, so authentication handlers read the tenant's options (`Configure<JwtBearerOptions>(scheme, …)` in Tenantry.Options' `ConfigurePerTenant`).

```csharp
public static IApplicationBuilder UseTenantResolution(this IApplicationBuilder app)
```

Parameters:

- `app` `IApplicationBuilder`: The application's request pipeline.

Returns: `IApplicationBuilder`: The same `app` for chaining.

Exceptions:

- `InvalidOperationException`: Tenantry is not registered with a way to resolve requests, or no tenant store is registered. The application also fails to start if [`TenantryApplicationBuilderExtensions.UseTenantry`](microsoft-aspnetcore-builder-tenantryapplicationbuilderextensions.md) is not in the pipeline.

Call it before `app.UseAuthentication()`, and [`TenantryApplicationBuilderExtensions.UseTenantry`](microsoft-aspnetcore-builder-tenantryapplicationbuilderextensions.md) after it, which runs the access validators, and the claim resolvers if nothing else named a tenant, then rejects or continues as it does alone. Between the two, the tenant is current but not yet checked against the user, so put only `app.UseAuthentication()` between them. An endpoint whose request did not pass through [`TenantryApplicationBuilderExtensions.UseTenantry`](microsoft-aspnetcore-builder-tenantryapplicationbuilderextensions.md) after this does not run: it gets `500` and log event 1011.

Only the resolvers added before the first that needs the user (a claim resolver, or `ResolveFromPropagationHeader`) run here, in order. If they find nothing, [`TenantryApplicationBuilderExtensions.UseTenantry`](microsoft-aspnetcore-builder-tenantryapplicationbuilderextensions.md) runs every resolver, in order, after authentication, so a resolver added after a claim resolver never wins over the claim; authentication then used the default settings.

```csharp
app.UseTenantResolution();
app.UseAuthentication();
app.UseTenantry();
app.UseAuthorization();
```

### `UseTenantry(IApplicationBuilder)`

Resolves each request's tenant, checks it, and makes it current for the rest of the request. A request to an endpoint that requires a tenant is rejected if it names none, or one that is unknown or refused.

```csharp
public static IApplicationBuilder UseTenantry(this IApplicationBuilder app)
```

Parameters:

- `app` `IApplicationBuilder`: The application's request pipeline.

Returns: `IApplicationBuilder`: The same `app` for chaining.

Exceptions:

- `InvalidOperationException`: Tenantry is not registered with a way to resolve requests (a `ResolveFrom…` or `UseResolver` method in `AddTenantry`), or no tenant store is registered.

Call it after `app.UseAuthentication()`, because claim resolvers and access validators read the user. Call it after routing, because it reads `RequireTenant()` and `AllowMissingTenant()` (`WebApplication` adds routing first). Call it before anything that needs the tenant. After [`TenantryApplicationBuilderExtensions.UseTenantResolution`](microsoft-aspnetcore-builder-tenantryapplicationbuilderextensions.md), it runs the access validators on the tenant found before authentication, and the claim resolvers if nothing else named a tenant.
