# `TenantryEndpointConventionBuilderExtensions` class

Namespace: `Microsoft.AspNetCore.Builder` · Package: `Tenantry.AspNetCore` · [API reference](README.md)

Extension methods for applying Tenantry endpoint metadata.

```csharp
public static class TenantryEndpointConventionBuilderExtensions
```

## Methods

### `AllowMissingTenant<TBuilder>(TBuilder)`

Allows the endpoint to execute without a resolved tenant, even when tenant resolution is required by default.

```csharp
public static TBuilder AllowMissingTenant<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
```

Type parameters:

- `TBuilder`: The endpoint convention builder type.

Parameters:

- `builder` `TBuilder`: The endpoint, or group of endpoints, to configure.

Returns: `TBuilder`: The same `builder` for chaining.

With both this and `RequireTenant` on an endpoint, the one added last wins. On `MapControllers()` or `MapRazorPages()`, it applies to the actions and pages without a [`RequireTenantAttribute`](tenantry-aspnetcore-requiretenantattribute.md) or [`AllowMissingTenantAttribute`](tenantry-aspnetcore-allowmissingtenantattribute.md) on their controller, action or page, which wins over it.

### `RequireTenant<TBuilder>(TBuilder)`

Requires Tenantry to resolve a tenant for the endpoint.

```csharp
public static TBuilder RequireTenant<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
```

Type parameters:

- `TBuilder`: The endpoint convention builder type.

Parameters:

- `builder` `TBuilder`: The endpoint, or group of endpoints, to configure.

Returns: `TBuilder`: The same `builder` for chaining.

With both this and `AllowMissingTenant` on an endpoint, the one added last wins. On `MapControllers()` or `MapRazorPages()`, it applies to the actions and pages without a [`RequireTenantAttribute`](tenantry-aspnetcore-requiretenantattribute.md) or [`AllowMissingTenantAttribute`](tenantry-aspnetcore-allowmissingtenantattribute.md) on their controller, action or page, which wins over it.
