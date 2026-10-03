# Troubleshooting

Common symptoms and what causes them. Most isolation surprises come down to one of: no tenant in scope,
the wrong `TKey`, middleware ordering, or the interceptor not attached.

## Startup fails with "has no tenant resolvers", "has no tenant store" or "found no tenant resolution"

`app.UseTenantry()` checks the registration when the pipeline is built. You called `AddTenantry` but forgot a
resolver or a store, or did not register Tenantry's request resolution at all.

- Add at least one resolver: `tenant.ResolveFromHeader(...)`, `ResolveFromClaim(...)`, etc., or
  `UseResolver(...)`.
- Add exactly one store: `tenant.UseInMemoryStore(...)` or `tenant.UseStore<T>()`.

In a worker or console app, `ITenantLookup` and `ITenantScopeFactory.RunInScopeAsync` throw the same "has no
tenant store" error; a hosted service that depends on the lookup throws it as the host starts.

## Startup fails with "app.UseTenantry() is not in the request pipeline"

You registered request resolution (a `ResolveFrom…` or `UseResolver` method in `AddTenantry`) but never called
`app.UseTenantry()`, so no request would have a tenant. Add it after `app.UseAuthentication()` and before your
endpoints (see [Pipeline ordering](aspnetcore-integration.md#pipeline-ordering)).

## Registration fails with "A tenant store is already registered" or "already registered with tenant key type"

An application has one store and one tenant key type. Remove the second `UseStore`/`UseInMemoryStore` (or the
`ITenantStore<TKey>` you registered yourself), and call `AddTenantry` with the same key type everywhere.

## Queries return **no** rows for a valid tenant

The query filter is fail-closed: when no tenant is current it matches nothing. Check, in order:

1. **Is a tenant actually in scope?** Inject `ITenantContext<TKey>` and confirm `HasTenant` is true at
   the point of the query. On the web, `UseTenantry()` must have run and resolved a tenant. In a
   worker, you must be inside a scope from `ITenantScopeFactory` (or `ITenantContextSetter.Use`).
2. **Did the request resolve a tenant?** A missing/blank header (or other source) means no tenant. If
   the endpoint should require one, add `.RequireTenant()` so you get a clear `400` instead of silent
   empties.
3. **Is the entity actually tenant-scoped?** It must implement `ITenantEntity<TKey>` with the **same**
   `TKey` you registered. A `Guid` registration plus an `ITenantEntity<string>` entity never lines up.
4. **Is the tenant id the default value?** `Guid.Empty`, `0` and an empty string mean "no tenant", so no
   tenant can have them: making such a tenant current throws `ArgumentException`.

## Queries return **all** tenants' rows

- You probably called `IgnoreQueryFilters()` somewhere (directly, or via a shared queryable helper).
- The entity does not implement `ITenantEntity<TKey>`, so it is treated as global. If it should be
  isolated, implement the interface (or derive from `TenantEntity<TKey>`).
- The context's options do not call `UseTenantry()`, so it has no isolation at all. Add it where the context is
  registered (`AddDbContext`, `AddDbContextPool`, `AddDbContextFactory`, `AddPooledDbContextFactory`).

## `TenantId` is not being stamped on insert

- The context's options do not call `UseTenantry()`, which attaches the interceptor.
- No tenant is in scope at `SaveChanges`. By default (`MissingTenantBehavior.Reject`) a save that writes
  tenant-scoped entities throws `TenantNotResolvedException` and nothing is written. `Warn` (which logs) and
  `Allow` let updates and deletes through unchecked, but a new entity must still carry its `TenantId`: without
  one the save throws. Save inside a tenant scope, or set `TenantId` yourself in deliberate cross-tenant code.

## `TenantIsolationViolationException` on save

This is the system working: a `Modified`/`Deleted` entity, or an `Added` one that names another tenant, belongs
to a different tenant than the current one. The exception's `Kind` is `EntityWrite`, and its
`OffendingTenantId` and `ExpectedTenantId` tell you which tenants.

- You loaded an entity in one tenant's scope and modified it in another's. Do tenant work inside the
  owning tenant's scope.
- You set `TenantId` manually to another tenant. Don't set it — let the interceptor stamp it.
- A legitimate cross-tenant admin operation: use a fresh `DbContext` inside the correct tenant's scope
  per tenant, or `IgnoreQueryFilters()` for reads. `SaveChanges` still validates every write against the
  current scope, but `IgnoreQueryFilters()` combined with `ExecuteUpdate`/`ExecuteDelete` affects every
  tenant, so treat it as privileged.

A message that **no row with its key is stored for the current tenant** comes from a read of the stored row, before
the save, of an owner or an entity over more than one table whose `TenantId` cannot be written back: the row is
another tenant's, or no longer exists. The read ignores your own query filters, so a soft-deleted row of the current
tenant's still passes.

## `Commit` throws `TenantIsolationViolationException`: "rolled back, not committed"

The exception's `Kind` is `TransactionRolledBack`. A `SaveChanges` in the transaction failed, or never ended, after
sending some of its statements, and among them were rows whose tenant another of its statements checks (owned rows in
a table of their own, or an entity over more than one table). The transaction has no savepoint for EF Core to undo
that save with (SQL Server with `MultipleActiveResultSets=True`), or EF Core failed to roll back to it, so those rows
may be in it, and Tenantry rolled it back instead. The save may have failed before EF Core read the check, so Tenantry
refuses whatever the failure was: a write to another tenant's row, a genuine concurrency conflict, or a duplicate key
in your own rows. Run the unit of work again. In a `TransactionScope`, the same failure rolls the ambient transaction
back, and disposing the scope throws `TransactionAbortedException`.

## `TenantIsolationViolationException` of kind `SaveWithoutTransaction`

`Database.AutoTransactionBehavior` is `Never`, `EfCoreIsolationOptions.OnSaveWithoutTransaction` is `Reject`, and the
save writes owned rows in a table of their own, or an entity over more than one table, whose tenant another of its
statements checks: without a transaction, a statement sent beside a check that failed would stay written. Nothing was
sent. Save such changes in a transaction, or set `OnSaveWithoutTransaction` back to `UseTransaction`, its default, so
EF Core runs these saves in a transaction of its own.

## `TenantIsolationViolationException`: "has no tenant query filter" or "is not a concurrency token"

The model check found a tenant-scoped entity type that would not be isolated, and stopped the query or save
before it ran. `UseTenantry()` adds the tenant filter and the concurrency token after `OnModelCreating`, so your
own configuration cannot remove them; something that runs after it did, such as a model-building convention, or
the model is one `UseTenantry()` did not build: a compiled model (`UseModel`), which it does not support.

## The model fails to build with `TenantIsolationViolationException` or "Tenantry is not registered"

`UseTenantry()` refuses a model it cannot isolate, when EF Core builds it (usually on the first query):

- **"implements ITenantEntity&lt;X&gt;, but '…' implements ITenantEntity&lt;Y&gt;":** use one tenant key type for
  every tenant-owned entity, the one you register with `AddTenantry`.
- **"Tenantry is not registered for 'X' tenant keys":** the context's application service provider has no
  `AddTenantry<X>`. Register Tenantry with your entities' key type, in the same service collection as the
  context.
- **"Entity … is tenant-owned but its base entity type … is not":** EF Core filters a hierarchy only through its
  root, so make the root tenant-owned too.
- **"Owned entity … is tenant-owned but its owner … is not":** EF Core filters owned rows only through their
  owner, so make the owner tenant-owned too.
- **"Owned entity … has no TenantId and a key that does not include its owner's key":** its rows carry no tenant,
  so Tenantry checks them through their owner, which a key of their own bypasses. Remove the `HasKey` so EF Core keys
  it by its owner, or implement `ITenantEntity<TKey>` on it.
- **"Entity … shares table … with tenant-owned … but is not tenant-owned"** (on the first query or save): implement
  `ITenantEntity<TKey>` on the entity that shares the table, or map it to a table of its own.
- **"Owned entity … is owned through a key of … that neither includes nor is part of its primary key, nor includes its
  TenantId":** own it through the owner's primary key (remove `HasPrincipalKey`), or through a key that includes
  `TenantId`.
- **"has no mapped public property 'TenantId'":** `TenantId` is implemented explicitly, not mapped, or of another
  type. Make it a public property of the key type; its setter can be private or init-only.
- **"has a query filter named 'Tenantry.Tenant'"** (EF Core 10): that name is the tenant filter's. Name your filter
  something else.
- **"Owned entity … is tenant-owned and mapped to JSON":** its owner's row holds it, under the owner's `TenantId`, and
  EF Core cannot check a `TenantId` of its own (EF Core 10 rejects the concurrency token itself). Remove
  `ITenantEntity<TKey>` from it: its owner isolates it.

## Creating the context fails with "replaces EF Core's IModelCustomizer" or "UseInternalServiceProvider"

`UseTenantry()` adds the tenant filters through its own model customizer, and EF Core checks the options when a
context is created:

- **"This context replaces EF Core's IModelCustomizer":** the options also call
  `ReplaceService<IModelCustomizer, …>()`. Move that configuration into `OnModelCreating` or an
  `ITenantModelContributor`.
- **"cannot be used with UseInternalServiceProvider"** (on EF Core 10, possibly EF Core's own error about
  `ISingletonInterceptor` services): EF Core adds no extension's services to an internal service provider you
  build. Remove `UseInternalServiceProvider`.

## `Reload()` detaches an entity, or `GetDatabaseValues()` returns `null`

EF Core reads an entity's database values by its key, and Tenantry keeps the tenant filter on that read, so a row
it cannot see reads as deleted: `GetDatabaseValues()` returns `null` and `Reload()` detaches the entity. The row
belongs to another tenant (a write whose key belongs to another tenant also matches no row, so EF Core throws
`DbUpdateConcurrencyException` first), or no tenant is current. On EF Core 8 and 9, the entity's own query filter is
merged with the tenant filter, so it applies to the read as well, and a soft-deleted row also reads as deleted; read
such a row with `IgnoreQueryFilters()` and a key and `TenantId` predicate of your own. On EF Core 10 your filters are
ignored, as EF Core documents. In a `DbUpdateConcurrencyException` handler, treat `null` as "not found".

## "has no application service provider"

The context was built by hand, without the application's services, so Tenantry cannot find the current tenant.
Register it with `AddDbContext` (or a pool or factory), or call `UseApplicationServiceProvider` on its options.
Building the model works without them, for design-time tools.

## Filter uses a stale tenant / leaks across requests

`UseTenantry()`'s filter reads the tenant through the context that runs each query, so it cannot go stale (see
[EF Core integration](efcore-integration.md#how-the-query-filter-stays-correct)). A filter of your own that
captures a tenant id, or an `ITenantContext<TKey>` instance, in `OnModelCreating` is evaluated once and reused
for every query: read the tenant through the context instead, or leave the tenant to `UseTenantry()`.

## Claim-based resolution or validation never matches

- `UseTenantry()` runs **before** `UseAuthentication()`, so `HttpContext.User` is empty when the
  resolver/validator runs. Move `UseTenantry()` after `UseAuthentication()`. For `ResolveFromClaim`, the middleware
  logs this once (event 1008, see [Diagnostics](diagnostics.md#logs)).
- The endpoint authenticates with a scheme that is not the default one (`[Authorize(AuthenticationSchemes = …)]`):
  its user is signed in by authorization, after `UseTenantry()`. Make that scheme the default.
- The claim type does not match (`ResolveFromClaim("tenant_id")` vs. the actual claim name).
- The claim value does not name a tenant: `ValidateTenantAccessByClaim` compares tenant ids, and a resolved
  claim is looked up like any identifier (e.g. a non-GUID string for a `Guid` key names no tenant).

## `AllowMissingTenant()` has no effect

The middleware ran before routing chose the endpoint, so `RequireTenantByDefault()` applied to it. It logs this once
(event 1007); call `app.UseRouting()` before `app.UseTenantry()`. An endpoint that requires a tenant still rejects a
request without one.

## Route-value resolution returns null

Routing must run before the middleware so the route value exists. With `WebApplication` this is
automatic; in a custom pipeline, ensure `UseRouting()` precedes `UseTenantry()`.

## Subdomain resolution returns null on localhost

Without a base domain, `ResolveFromSubdomain` requires at least three dot-separated host segments, so `localhost`
and `acme.localhost` resolve to `null`. Add one for development:
`tenant.ResolveFromSubdomain(o => o.BaseDomains.Add("localhost"))` resolves `acme.localhost` to `acme`.

## Subdomains resolve, but name no tenant (`404`)

With `Guid` or `int` keys, `acme` is not a tenant id: the default `FindByIdentifierAsync` parses the identifier as
the key type, so it names no tenant. Implement `FindByIdentifierAsync` in your store to map slugs (see
[Identifiers other than the id](tenant-resolution.md#identifiers-other-than-the-id)).

## A suspended tenant is still served

With `CacheTenants`, the cached tenant is served until its entry expires. Call `ITenantStoreCache<TKey>.Invalidate`
when you change a tenant (see [Caching](tenant-stores.md#caching)).

## Requests have no `tenant.id` tag or `TenantId` log scope

The tag is on the request's trace span, which exists only with tracing (OpenTelemetry's ASP.NET Core
instrumentation, for example). The scope needs a logging provider that records scopes (`IncludeScopes`). Both are
added only to requests that resolve a tenant. See [Diagnostics](diagnostics.md).

## Background/queued work loses the tenant

The `AsyncLocal` tenant flows down into awaited work but does not survive past the scope's disposal.
Capture the tenant **id** when enqueuing and run the deferred work with
`ITenantScopeFactory.RunInScopeAsync(id, …)`. See [Non-HTTP hosts](non-http-hosts.md#scopes-async-and-threads).

## The tenant is missing after a helper opened a scope

A scope opened inside an `async` method is not active for that method's caller: an `async` method's
changes to an `AsyncLocal` are undone when it returns. Open the scope in the method that does the work
(`await using var scope = scopes.CreateScope(tenant);`), or pass the work in with
`ITenantScopeFactory.RunInScopeAsync`.

## AOT/trim warnings from the EF Core integration

Expected. EF Core's `DbContext` is annotated `[RequiresDynamicCode]` and `[RequiresUnreferencedCode]`, and
`UseTenantry()` builds its query filters as expression trees while EF Core builds the model. EF Core is not
AOT-compatible; do not publish an EF-Core-backed app with Native AOT. See
[AOT & trimming](aot-and-trimming.md).
