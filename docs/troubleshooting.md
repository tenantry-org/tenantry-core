# Troubleshooting

Common symptoms and what causes them. Most isolation surprises come down to one of: no tenant in scope,
the wrong `TKey`, middleware ordering, or the interceptor not attached.

## Startup fails with "has no tenant resolvers", "has no tenant store" or "found no tenant resolution"

`app.UseTenantry()` checks the registration when the pipeline is built. You called `AddTenantry` but forgot a
resolver or a store, or did not register Tenantry's request resolution at all.

- Add at least one resolver: `tenant.ResolveFromHeader(...)`, `ResolveFromClaim(...)`, etc., or
  `UseResolver(...)`.
- Add exactly one store: `tenant.UseInMemoryStore(...)` or `tenant.UseStore<T>()`.

In a worker or console app, `ITenantStoreAccessor` and `ITenantScopeFactory.RunInScopeAsync` throw the same "has no
tenant store" error; a hosted service that depends on the accessor throws it as the host starts.

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

## `TenantIsolationViolationException`: "has no tenant query filter" or "is not a concurrency token"

The model check found a tenant-scoped entity type that would not be isolated, and stopped the query or save
before it ran. `UseTenantry()` adds the tenant filter and the concurrency token after `OnModelCreating`, so your
own configuration cannot remove them; something that runs after it did: an `IModelCustomizer` put in place
through a custom internal service provider (`UseInternalServiceProvider`), a model-building convention, or a
compiled model (`dotnet ef dbcontext optimize`) built without them. Let `UseTenantry()` build the model.

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
- **"This context replaces EF Core's IModelCustomizer":** the options also call
  `ReplaceService<IModelCustomizer, …>()`. Move that configuration into `OnModelCreating` or an
  `ITenantModelContributor`.

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
  resolver/validator runs. Move `UseTenantry()` after `UseAuthentication()`.
- The claim type does not match (`ResolveFromClaim("tenant_id")` vs. the actual claim name).
- The claim value does not parse to your `TKey` (e.g. a non-GUID string for a `Guid` key).

## Route-value resolution returns null

Routing must run before the middleware so the route value exists. With `WebApplication` this is
automatic; in a custom pipeline, ensure `UseRouting()` precedes `UseTenantry()`.

## Subdomain resolution returns null on localhost

`ResolveFromSubdomain` requires at least three dot-separated host segments, so `localhost` and
`acme.localhost` resolve to `null`. Use header resolution for local development.

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
