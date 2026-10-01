# `ITenantDescriptor` interface

Namespace: `Tenantry` · Package: `Tenantry.Core` · [API reference](README.md)

A tenant, without its identifier type: the base of [`ITenantDescriptor<TKey>`](tenantry-itenantdescriptor-1.md), for code that does not need the tenant's id, such as [`TenantDescriptorExtensions.As<TTenant>`](tenantry-tenantdescriptorextensions.md).

```csharp
public interface ITenantDescriptor
```

Derived types: [`TenantDescriptor<TKey>`](tenantry-tenantdescriptor.md).

## Properties

### `Name`

Human-readable display name for the tenant.

```csharp
string Name { get; }
```

Value: `string`
