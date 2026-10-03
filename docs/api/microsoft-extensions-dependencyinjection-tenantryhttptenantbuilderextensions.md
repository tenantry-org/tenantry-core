# `TenantryHttpTenantBuilderExtensions` class

Namespace: `Microsoft.Extensions.DependencyInjection` · Package: `Tenantry.Http` · [API reference](README.md)

Sends the current tenant to the services an application calls over HTTP or gRPC.

```csharp
public static class TenantryHttpTenantBuilderExtensions
```

## Methods

### `AddHttpPropagation<TKey>(ITenantBuilder<TKey>)`

Lets the HTTP and gRPC clients marked with [`TenantryHttpClientBuilderExtensions.UseTenantry`](microsoft-extensions-dependencyinjection-tenantryhttpclientbuilderextensions.md) send the current tenant's id to the services they call, in the [`TenantPropagation.HeaderName`](tenantry-tenantpropagation.md) header. The receiving service reads it with Tenantry.AspNetCore's `ResolveFromPropagationHeader(...)`.

```csharp
public static ITenantBuilder<TKey> AddHttpPropagation<TKey>(this ITenantBuilder<TKey> builder) where TKey : IEquatable<TKey>, IParsable<TKey>
```

Type parameters:

- `TKey`: The tenant identifier type.

Parameters:

- `builder` [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The tenant builder.

Returns: [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The same `builder` for chaining.

```csharp
builder.Services.AddTenantry<Guid>(tenant => tenant
    .UseStore<AppTenantStore>()
    .AddHttpPropagation());

builder.Services.AddHttpClient<BillingClient>(c => c.BaseAddress = new Uri("https://billing.internal"))     .UseTenantry(); ```
