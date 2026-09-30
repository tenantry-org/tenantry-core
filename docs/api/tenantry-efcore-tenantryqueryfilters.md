# `TenantryQueryFilters` class

Namespace: `Tenantry.EfCore` · Package: `Tenantry.EfCore` · [API reference](README.md)

The names of the query filters Tenantry adds.

```csharp
public static class TenantryQueryFilters
```

## Fields

### `Tenant`

The name of the tenant query filter `UseTenantry()` adds on EF Core 10 and later. Pass it to `IgnoreQueryFilters([TenantryQueryFilters.Tenant])` to read every tenant's rows while keeping your other named filters, such as a soft-delete filter.

```csharp
public const string Tenant = "Tenantry.Tenant"
```

Returns: `string`

When an entity type also has an unnamed filter, EF Core 10 does not allow a named one beside it, so Tenantry merges the tenant filter into the unnamed filter instead (and logs this once for the model), and only `IgnoreQueryFilters()` removes it, together with your own. Name your filters to keep them apart. EF Core 8 and 9 have no named filters: there the tenant filter is always merged into the entity's filter.
