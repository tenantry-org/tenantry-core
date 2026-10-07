# `TenantDescriptorExtensions` class

Namespace: `Tenantry` · Package: `Tenantry.Core` · [API reference](README.md)

Reads a tenant as the application's own tenant type.

```csharp
public static class TenantDescriptorExtensions
```

## Methods

### `As<TTenant>(ITenantDescriptor)`

Returns `tenant` as `TTenant`, the type your tenant store returns, so a delegate that receives an [`ITenantDescriptor<TKey>`](tenantry-itenantdescriptor-1.md) can read your tenant's own properties.

```csharp
public static TTenant As<TTenant>(this ITenantDescriptor tenant) where TTenant : class, ITenantDescriptor
```

Type parameters:

- `TTenant`: Your tenant type, which implements [`ITenantDescriptor<TKey>`](tenantry-itenantdescriptor-1.md).

Parameters:

- `tenant` [`ITenantDescriptor`](tenantry-itenantdescriptor.md): A tenant from your tenant store.

Returns: `TTenant`: The same tenant, as `TTenant`.

Exceptions:

- `InvalidOperationException`: The tenant is not a `TTenant`: the tenant store returns another type. The message names both types.

```csharp
tenant.UseConnectionStrings(options =>
    options.GetConnectionString = t => t.As<AppTenant>().ConnectionString);
```
