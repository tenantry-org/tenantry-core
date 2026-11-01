# `TenantryWarnings` class

Namespace: `Tenantry` · Package: `Tenantry.Core` · [API reference](README.md)

The warnings an application can turn off with `tenant.IgnoreWarnings(…)`: each reports, once, configuration that may be deliberate. The documentation's diagnostics page lists every event Tenantry logs.

```csharp
public static class TenantryWarnings
```

## Fields

### `OrdinaryOptionsReadAsTenant`

Event 2008, from Tenantry.Options: `IOptions<T>` of a type configured per tenant was read while a tenant is current, and gave the ordinary value.

```csharp
public const int OrdinaryOptionsReadAsTenant = 2008
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

Whether the application turned off the warning `eventId` with `IgnoreWarnings`. Tenantry's packages read it before they log one of the warnings this class names, and an integration before it logs one it registered.

```csharp
public static bool IsIgnored(IServiceProvider services, int eventId)
```

Parameters:

- `services` `IServiceProvider`: The application's services.
- `eventId` `int`: The warning's event id.

Returns: `bool`: [true](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/bool) when the warning is not to be logged.

### `Register(IServiceCollection, params int[])`

Registers the warnings an integration logs that the application may turn off, so `IgnoreWarnings` accepts their event ids. Call it from the integration's registration method, which the application calls before `IgnoreWarnings`; the ids are from the range the documentation's diagnostics page leaves for integrations.

```csharp
public static void Register(IServiceCollection services, params int[] eventIds)
```

Parameters:

- `services` `IServiceCollection`: The application's services.
- `eventIds` `int[]`: The event ids of the integration's warnings: 3000 to 9999 are Tenantry.Pro's, and an integration by others uses 10000 and above.

Exceptions:

- `ArgumentNullException`: `services` or `eventIds` is null.
- `ArgumentOutOfRangeException`: An id is below 3000, in the range of Tenantry's own events.
