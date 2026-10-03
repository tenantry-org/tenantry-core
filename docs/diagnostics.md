# Diagnostics

Tenantry writes logs with stable event ids, tags the request's trace span with its tenant, and counts how requests
were resolved. None of it needs setting up beyond your logging, tracing and metrics pipelines.

## Logs

Tenantry logs under two categories, `Tenantry.AspNetCore` (the request middleware) and `Tenantry.EfCore` (the
isolation in your `DbContext`). Each message has an event id that does not change between versions, so you can
alert on it. Alert on **2001** above all: a save that tried to write another tenant's row.

| Event id | Name | Level | When |
|----------|------|-------|------|
| 1001 | `TenantResolved` | Debug | A request's tenant is made current. |
| 1002 | `NoTenantIdentifier` | Debug | A request carries no identifier, and its endpoint does not require a tenant. |
| 1003 | `TenantRequired` | Warning | A request carries no identifier, and its endpoint requires a tenant. |
| 1004 | `TenantNotFound` | Warning | A request's identifier names no tenant, and its endpoint requires a tenant. |
| 1005 | `TenantAccessDenied` | Warning | An access validator refuses a request's tenant. |
| 1006 | `ContinuingWithoutTenant` | Debug | A request's identifier names no tenant, or one it may not use, and its endpoint does not require a tenant. |
| 1007 | `TenantryBeforeRouting` | Warning | `app.UseTenantry()` ran before routing chose an endpoint with `RequireTenant()` or `AllowMissingTenant()`. Logged once. |
| 1008 | `TenantryBeforeAuthentication` | Warning | The authentication middleware ran after `app.UseTenantry()` and signed in a user with the claim `ResolveFromClaim` reads, which it therefore missed. Logged once. |
| 2001 | `TenantIsolationViolation` | Error | `SaveChanges` refused to write an entity of another tenant. |
| 2002 | `WriteWithoutTenant` | Warning | `SaveChanges` wrote tenant-owned entities without a tenant, under `OnMissingTenant = Warn`. |
| 2003 | `WriteMatchedNoRow` | Warning | An update or delete of a tenant-owned entity matched no row: it does not exist, belongs to another tenant, or changed concurrently. |
| 2004 | `TransactionNotCommitted` | Error | A save whose rows rely on another of its statements' tenant check failed, or never ended, after sending some of them, in a transaction EF Core could not roll that save back in (no savepoint, or a `TransactionScope`): the transaction is rolled back instead of committed. |
| 2005 | `SaveInTransaction` | Debug | A save whose rows rely on another of its statements' tenant check runs in a transaction although `AutoTransactionBehavior` is `Never` (`OnSaveWithoutTransaction = UseTransaction`). |

While a request's tenant is current, a log scope with one property, `TenantId`, is open, so every entry the request
writes carries it. A logging provider adds scope properties to its entries when it records scopes: Serilog's, or the
console's and OpenTelemetry's with `IncludeScopes`. Tenantry.Pro's jobs and messages open the same scope.

```csharp
builder.Logging.AddJsonConsole(o => o.IncludeScopes = true);
```

## Traces

The middleware tags the request's span (ASP.NET Core's, which OpenTelemetry's ASP.NET Core instrumentation
records) with `tenant.id`, the tenant's id formatted with the invariant culture. A request without a tenant gets no
tag. Tenantry.Pro tags its jobs' and messages' spans the same way.

It also starts a span of its own, `Tenantry.ResolveTenant`, on the `Tenantry.AspNetCore` activity source, around the
resolvers, the store lookup and the access validators. It has the tags `tenantry.resolution.result` (`resolved`,
`missing`, `not_found` or `access_denied`) and, when resolved, `tenant.id`. Add the source to record it:

```csharp
using OpenTelemetry.Trace;

builder.Services.AddOpenTelemetry()
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation()
        .AddSource("Tenantry.AspNetCore"));
```

## Metrics

The `Tenantry.AspNetCore` meter has one instrument:

| Instrument | Type | Unit | Tags |
|------------|------|------|------|
| `tenantry.resolutions` | Counter | `{request}` | `tenantry.resolution.result` (`resolved`, `missing`, `not_found`, `access_denied`); `tenantry.resolution.rejected` (`true` when the request was refused) |

It counts every request the middleware handles, including the ones an endpoint that requires a tenant refuses,
which never reach your endpoints. It has no `tenant.id` tag, to keep its series few: Tenantry.Pro's tenant metrics
add the tenant to ASP.NET Core's own request metric.

```csharp
using OpenTelemetry.Metrics;

builder.Services.AddOpenTelemetry()
    .WithMetrics(m => m.AddMeter("Tenantry.AspNetCore"));
```

```bash
dotnet-counters monitor --counters Tenantry.AspNetCore --process-id <pid>
```
