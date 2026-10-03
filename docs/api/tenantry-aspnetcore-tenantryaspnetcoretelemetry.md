# `TenantryAspNetCoreTelemetry` class

Namespace: `Tenantry.AspNetCore` · Package: `Tenantry.AspNetCore` · [API reference](README.md)

The names `app.UseTenantry()` records requests under, for OpenTelemetry's `AddSource` and `AddMeter`, and for log filters. The request's tenant itself goes under [`TenantTelemetry`](tenantry-tenanttelemetry.md)'s names.

```csharp
public static class TenantryAspNetCoreTelemetry
```

## Fields

### `ActivitySourceName`

The activity source of the `Tenantry.ResolveTenant` span: `Tenantry.AspNetCore`.

```csharp
public const string ActivitySourceName = "Tenantry.AspNetCore"
```

Returns: `string`

### `LogCategory`

The category of the middleware's log messages: `Tenantry.AspNetCore`.

```csharp
public const string LogCategory = "Tenantry.AspNetCore"
```

Returns: `string`

### `MeterName`

The meter of the `tenantry.resolutions` counter: `Tenantry.AspNetCore`.

```csharp
public const string MeterName = "Tenantry.AspNetCore"
```

Returns: `string`
