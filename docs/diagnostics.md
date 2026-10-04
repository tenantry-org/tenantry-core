# Diagnostics

Tenantry writes logs with stable event ids, tags the request's trace span with its tenant, and counts how requests
were resolved. None of it needs setting up beyond your logging, tracing and metrics pipelines.

## Logs

Tenantry logs under three categories: `Tenantry.AspNetCore` (the request middleware), `Tenantry.EfCore` (the isolation
in your `DbContext`) and `Tenantry.Options` (options per tenant). Each message has an event id that does not change
between versions, so you can alert on it. Alert on 2001 above all: a save that tried to write another tenant's row.

| Event id | Name | Level | When |
|----------|------|-------|------|
| 1001 | `TenantResolved` | Debug | A request's tenant is made current. |
| 1002 | `NoTenantIdentifier` | Debug | A request carries no identifier, and its endpoint does not require a tenant. |
| 1003 | `TenantRequired` | Warning | A request carries no identifier, and its endpoint requires a tenant. |
| 1004 | `TenantNotFound` | Warning | A request's identifier names no tenant, and its endpoint requires a tenant. |
| 1005 | `TenantAccessDenied` | Warning | An access validator refuses a request's tenant. |
| 1006 | `ContinuingWithoutTenant` | Debug | A request's identifier names no tenant, one that is not active, or one it may not use, and its endpoint does not require a tenant. |
| 1007 | `TenantryBeforeRouting` | Warning | `app.UseTenantry()` ran before routing chose an endpoint with `RequireTenant()` or `AllowMissingTenant()`. Requests without a tenant are still rejected where one is required. Logged once. |
| 1008 | `TenantryBeforeAuthentication` | Warning | The authentication middleware ran after `app.UseTenantry()` and signed in a user with the claim `ResolveFromClaim` reads, which it therefore missed. Logged once. |
| 1009 | `OutputCacheBeforeTenantry` | Warning | The output cache ran before `app.UseTenantry()` for a request, so `IsolateOutputCache()` did not cache its response. Logged once. |
| 1010 | `TenantResolutionAfterAuthentication` | Warning | `app.UseTenantResolution()` ran after the authentication middleware, so authentication used no tenant's settings. Logged once. |
| 1011 | `TenantryDidNotRun` | Error | `app.UseTenantResolution()` resolved a request, but `app.UseTenantry()` did not run before its endpoint, which was not run (500). |
| 1012 | `TenantInactive` | Warning | A request's tenant is not active (`ValidateTenantActivity`). |
| 2001 | `TenantIsolationViolation` | Error | `SaveChanges` refused to write an entity of another tenant. |
| 2002 | `WriteWithoutTenant` | Warning | `SaveChanges` wrote tenant-owned entities without a tenant, under `OnMissingTenant = Warn`. |
| 2003 | `WriteMatchedNoRow` | Warning | An update or delete of a tenant-owned entity matched no row: it does not exist, belongs to another tenant, or changed concurrently. |
| 2004 | `TransactionNotCommitted` | Error | A save failed, or never finished, after sending statements in a transaction where a save wrote rows that depend on another of its statements' tenant check. EF Core could not roll back only that save (no savepoint, or a `TransactionScope`), so the transaction is rolled back instead of committed. `EntityType` is the entity whose check failed, or the context's type for any other failure, such as a caught save failure the application went on after. |
| 2005 | `SaveInTransaction` | Debug | A save whose rows rely on another of its statements' tenant check runs in a transaction although `AutoTransactionBehavior` is `Never` (`OnSaveWithoutTransaction = UseTransaction`). |
| 2006 | `UnclassifiedEntityTypes` | Warning | A context's model has entity types that are neither tenant-owned nor marked as shared across tenants, under `OnUnclassifiedEntityType = Warn`. Logged once for each model EF Core builds: usually once per context type, and again if EF Core drops the model from its cache and builds it again. |
| 3001 | `OrdinaryOptionsReadAsTenant` | Warning | `IOptions<T>` of a type configured per tenant was read while a tenant is current. It gives the ordinary value, so the code most likely wants `IOptionsSnapshot<T>` or `IOptionsMonitor<T>`. Logged once per options type. |

While a request's tenant is current, a log scope with one property, `TenantId`, is open, so every entry the request
writes carries it. A logging provider adds scope properties to its entries when it records scopes: Serilog's, or the
console's and OpenTelemetry's with `IncludeScopes`. Tenantry.Pro's jobs and messages open the same scope.

```csharp
builder.Logging.AddJsonConsole(o => o.IncludeScopes = true);
```

The names are public, in `TenantTelemetry`, so your own code can record the tenant the same way: `TenantIdTag`
(`tenant.id`), `LogScopeName` (`TenantId`), and `CreateLogScope`, the scope's state, which formats the id as Tenantry
does (`TenantIds.Format`).

```csharp
using Tenantry;

using (logger.BeginScope(TenantTelemetry.CreateLogScope(tenantId)))
{
    logger.LogInformation("Invoicing");
}
```

## Traces

The middleware tags the request's span (ASP.NET Core's, which OpenTelemetry's ASP.NET Core instrumentation
records) with `tenant.id`, the tenant's id formatted with the invariant culture. A request without a tenant gets no
tag. Tenantry.Pro tags its jobs' and messages' spans the same way.

It also starts a span of its own, `Tenantry.ResolveTenant`, on the `Tenantry.AspNetCore` activity source, around the
resolvers, the store lookup and the access validators. It has the tags `tenantry.resolution.result` (`resolved`,
`missing`, `not_found`, `access_denied` or `inactive`) and, when resolved, `tenant.id`. Add the source to record it:

```csharp
using OpenTelemetry.Trace;
using Tenantry.AspNetCore;

builder.Services.AddOpenTelemetry()
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation()
        .AddSource(TenantryAspNetCoreTelemetry.ActivitySourceName));
```

## Metrics

The `Tenantry.AspNetCore` meter has one instrument:

| Instrument | Type | Unit | Tags |
|------------|------|------|------|
| `tenantry.resolutions` | Counter | `{request}` | `tenantry.resolution.result` (`resolved`, `missing`, `not_found`, `access_denied`, `inactive`); `tenantry.resolution.rejected` (`true` when the request was refused) |

It counts every request the middleware handles, including the ones an endpoint that requires a tenant refuses,
which never reach your endpoints. It has no `tenant.id` tag, to keep its series few.

```csharp
using OpenTelemetry.Metrics;

builder.Services.AddOpenTelemetry()
    .WithMetrics(m => m.AddMeter(TenantryAspNetCoreTelemetry.MeterName));
```

```bash
dotnet-counters monitor --counters Tenantry.AspNetCore --process-id <pid>
```

### Request metrics per tenant

`TagRequestMetrics()` adds the tenant to ASP.NET Core's own request metric, `http.server.request.duration`, as
`tenant.id`, so latency and errors can be read per tenant. A request without a tenant gets no tag.

```csharp
builder.Services.AddTenantry<string>(tenant => tenant
    .ResolveFromSubdomain()
    .UseStore<AppTenantStore>()
    .TagRequestMetrics());
```

Each tag value is a series of its own for every route, method and status code. With many tenants, tag the ones you
watch and group the rest: the function returns the tag for a tenant, or `null` to leave it off.

```csharp
tenant.TagRequestMetrics(t => t.TenantId.StartsWith("enterprise-") ? t.TenantId : "other");
```
