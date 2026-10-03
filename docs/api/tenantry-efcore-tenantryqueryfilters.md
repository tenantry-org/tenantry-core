# `TenantryQueryFilters` class

Namespace: `Tenantry.EfCore` · Package: `Tenantry.EfCore` · [API reference](README.md)

The names of the query filters Tenantry adds or names on EF Core 10 and later.

EF Core 8 and 9 have no named filters: there the tenant filter is merged into the entity's own filter, and only `IgnoreQueryFilters()` removes it, together with your own.

```csharp
public static class TenantryQueryFilters
```

## Fields

### `Application`

The name an entity's unnamed query filter gets on EF Core 10 and later. EF Core does not allow a named filter beside an unnamed one, so `UseTenantry()` names yours, to add the tenant filter beside it rather than merge the two. Your filter still applies to every query; pass this name to `IgnoreQueryFilters` to remove it alone, or name your filters yourself.

```csharp
public const string Application = "Tenantry.Application"
```

Returns: `string`

### `Tenant`

The name of the tenant query filter `UseTenantry()` adds on EF Core 10 and later. Pass it to `IgnoreQueryFilters([TenantryQueryFilters.Tenant])` to read every tenant's rows while keeping your other filters, such as a soft-delete filter.

```csharp
public const string Tenant = "Tenantry.Tenant"
```

Returns: `string`
