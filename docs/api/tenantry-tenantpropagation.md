# `TenantPropagation` class

Namespace: `Tenantry` · Package: `Tenantry.Core` · [API reference](README.md)

How Tenantry carries a tenant from one process to another: Tenantry.Http's outgoing requests, Tenantry.AspNetCore's `ResolveFromPropagationHeader(...)` on the receiving side, and Tenantry.Pro's Hangfire, MassTransit, Quartz.NET and Rebus integrations.

```csharp
public static class TenantPropagation
```

## Fields

### `HeaderName`

The name of the HTTP header, the Hangfire job parameter, the MassTransit or Rebus message header, or the Quartz.NET job data key that carries the tenant's id, formatted by [`TenantIds.Format<TKey>`](tenantry-tenantids.md): `tenantry-tenant-id`.

```csharp
public const string HeaderName = "tenantry-tenant-id"
```

Returns: `string`
