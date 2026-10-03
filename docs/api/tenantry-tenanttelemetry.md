# `TenantTelemetry` class

Namespace: `Tenantry` · Package: `Tenantry.Core` · [API reference](README.md)

The names Tenantry records a tenant under in traces and logs: Tenantry.AspNetCore's `app.UseTenantry()` for a request, and Tenantry.Pro for jobs, messages and background work. Use them to record the tenant the same way in your own code, or to query and alert on it.

```csharp
public static class TenantTelemetry
```

## Fields

### `LogScopeName`

The property that carries the tenant's id in a log scope: `TenantId`, the id formatted by [`TenantIds.Format<TKey>`](tenantry-tenantids.md).

```csharp
public const string LogScopeName = "TenantId"
```

Returns: `string`

### `TenantIdTag`

The tag that carries the tenant's id on a span (an `Activity`) or a metric: `tenant.id`, the id formatted by [`TenantIds.Format<TKey>`](tenantry-tenantids.md).

```csharp
public const string TenantIdTag = "tenant.id"
```

Returns: `string`

## Methods

### `CreateLogScope(string)`

The state of a log scope with one property, [`TenantTelemetry.LogScopeName`](tenantry-tenanttelemetry.md), for `ILogger.BeginScope`. A logging provider that records scopes (Serilog's, or the console's and OpenTelemetry's with `IncludeScopes`) adds the property to every entry written while the scope is open; its text is `TenantId:<id>`.

```csharp
public static IReadOnlyList<KeyValuePair<string, object?>> CreateLogScope(string tenantId)
```

Parameters:

- `tenantId` `string`: The tenant's id, formatted by [`TenantIds.Format<TKey>`](tenantry-tenantids.md).

Returns: `IReadOnlyList<KeyValuePair<string, object>>`: The scope's state.

Exceptions:

- `ArgumentNullException`: `tenantId` is null.

```csharp
using (logger.BeginScope(TenantTelemetry.CreateLogScope(TenantIds.Format(tenantId))))
{
    logger.LogInformation("Invoicing");   // carries TenantId
}
```
