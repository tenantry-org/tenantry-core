# `ITenantScope<TKey>` interface

Namespace: `Tenantry.Core` · Package: `Tenantry.Core` · [API reference](README.md)

Represents a scoped interface for managing tenant-specific context within the application. It provides mechanisms to activate and maintain a tenant context within the current execution flow, such as for HTTP requests or background tasks.

```csharp
public interface ITenantScope<TKey> : ITenantContext<TKey> where TKey : IEquatable<TKey>, IParsable<TKey>
```

## Type parameters

- `TKey`: The type used for tenant identification. This type must support equality comparison and parsing operations.

## Methods

### `BeginScope(ITenantDescriptor<TKey>)`

Activates a tenant for the current execution context (e.g. an HTTP request or background job). Scopes may nest: an inner scope shadows the outer tenant, and disposing it restores the outer tenant (the outermost scope restores "no tenant"). The returned `IDisposable` restores the previously active tenant when disposed.

```csharp
IDisposable BeginScope(ITenantDescriptor<TKey> tenant)
```

Parameters:

- `tenant` [`ITenantDescriptor<TKey>`](tenantry-core-itenantdescriptor.md): The tenant to activate.

Returns: `IDisposable`: A handle that restores the previously active tenant on disposal.
