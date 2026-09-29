# `ApplicationBuilderExtensions` class

Namespace: `Tenantry.AspNetCore.Extensions` · Package: `Tenantry.AspNetCore` · [API reference](README.md)

Extension methods for adding Tenantry middleware to the request pipeline.

```csharp
public static class ApplicationBuilderExtensions
```

## Methods

### `UseTenantry(IApplicationBuilder)`

Adds the tenant resolution middleware to the pipeline. Must be called after authentication middleware (`app.UseAuthentication()`) if using claim-based resolution, and before any middleware that requires a resolved tenant (e.g. authorisation, controllers). When using `RequireTenant()` or `AllowMissingTenant()` endpoint metadata, ensure routing has executed before this middleware. `WebApplication` handles this automatically for minimal APIs and controllers.

```csharp
public static IApplicationBuilder UseTenantry(this IApplicationBuilder app)
```

Parameters:

- `app` `IApplicationBuilder`: The application's request pipeline.

Returns: `IApplicationBuilder`
