# `TenantryWarnings` class

Namespace: `Tenantry` · Package: `Tenantry.Core` · [API reference](README.md)

The warnings an application can turn off with `tenant.IgnoreWarnings(…)`: each reports, once, configuration that may be deliberate. The documentation's diagnostics page lists every event Tenantry logs.

```csharp
public static class TenantryWarnings
```

## Fields

### `OrdinaryOptionsReadAsTenant`

Event 3001, from Tenantry.Options: `IOptions<T>` of a type configured per tenant was read while a tenant is current, and gave the ordinary value.

```csharp
public const int OrdinaryOptionsReadAsTenant = 3001
```

Returns: `int`

### `StringTenantIdCollation`

Event 2007, from Tenantry.EfCore: on SQL Server or MySQL, a model's `string` `TenantId` columns have no collation, so the database's default, which ignores case, compares tenant ids.

```csharp
public const int StringTenantIdCollation = 2007
```

Returns: `int`

## Methods

### `IsIgnored(IServiceProvider, int)`

Whether the application turned off the warning `eventId` with `IgnoreWarnings`. A package that logs warnings of its own reads it here.

```csharp
public static bool IsIgnored(IServiceProvider services, int eventId)
```

Parameters:

- `services` `IServiceProvider`: The application's services.
- `eventId` `int`: The warning's event id.

Returns: `bool`: [true](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/bool) when the warning is not to be logged.
