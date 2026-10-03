# `TenantIds` class

Namespace: `Tenantry` · Package: `Tenantry.Core` · [API reference](README.md)

Tenant ids as text, and the ids Tenantry reserves for "no tenant". Tenantry formats and parses tenant ids this way wherever they leave or enter the process: in log scopes and traces, in the headers Tenantry.Http and Tenantry.Pro's jobs and messages carry, and in the identifiers [`ITenantStore<TKey>.FindByIdentifierAsync`](tenantry-itenantstore.md) reads by default.

```csharp
public static class TenantIds
```

## Methods

### `Format<TKey>(TKey)`

Formats a tenant id with the invariant culture, so it reads the same on every machine: `42`, a GUID's `D` format, or a string as it is.

```csharp
public static string Format<TKey>(TKey tenantId) where TKey : IEquatable<TKey>, IParsable<TKey>
```

Type parameters:

- `TKey`: The tenant identifier type.

Parameters:

- `tenantId` `TKey`: The tenant id.

Returns: `string`: The id as text.

Exceptions:

- `ArgumentNullException`: `tenantId` is null.

### `IsReserved<TKey>(TKey?)`

Returns true when `tenantId` is [null](https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/null), the key type's default (`Empty`, `0`) or an empty string. [`ITenantContext<TKey>.CurrentTenantId`](tenantry-itenantcontext.md) has that value when no tenant is current, so no tenant can have it, and an entity whose `TenantId` has it belongs to no tenant yet.

```csharp
public static bool IsReserved<TKey>(TKey? tenantId) where TKey : IEquatable<TKey>, IParsable<TKey>
```

Type parameters:

- `TKey`: The tenant identifier type.

Parameters:

- `tenantId` `TKey`: The tenant id.

Returns: `bool`: Whether the id is one Tenantry reserves for "no tenant".

### `TryParse<TKey>(string?, out TKey)`

Parses `text` as a tenant id with the invariant culture, as [`TenantIds.Format<TKey>`](tenantry-tenantids.md) writes it. Returns false for text that does not parse and for the ids Tenantry reserves for "no tenant" ([`TenantIds.IsReserved<TKey>`](tenantry-tenantids.md)), so a parsed id can name a tenant.

```csharp
public static bool TryParse<TKey>(string? text, out TKey tenantId) where TKey : IEquatable<TKey>, IParsable<TKey>
```

Type parameters:

- `TKey`: The tenant identifier type.

Parameters:

- `text` `string`: The text, from a header, a route value or a claim.
- `tenantId` `TKey`: The parsed id, when this returns true.

Returns: `bool`: Whether `text` is a tenant id.
