# API reference

Every public type in the packages, generated from their XML documentation comments. The guides explain how
the pieces fit together; this reference is for the details of each type and member.

## Tenantry.AspNetCore

### `Tenantry.AspNetCore`

| Type | Kind | Summary |
|------|------|---------|
| [`IAspNetCoreTenantBuilder<TKey>`](tenantry-aspnetcore-iaspnetcoretenantbuilder.md) | interface | Fluent builder for configuring multi-tenancy services. Obtained from [`ServiceCollectionExtensions.AddTenantry<TKey>`](tenantry-aspnetcore-extensions-servicecollectionextensions.md). |

### `Tenantry.AspNetCore.Attributes`

| Type | Kind | Summary |
|------|------|---------|
| [`AllowMissingTenantAttribute`](tenantry-aspnetcore-attributes-allowmissingtenantattribute.md) | class | Allows an endpoint or controller action to execute without a resolved tenant. |
| [`RequireTenantAttribute`](tenantry-aspnetcore-attributes-requiretenantattribute.md) | class | Requires Tenantry to resolve a tenant before the endpoint or controller action executes. |

### `Tenantry.AspNetCore.Extensions`

| Type | Kind | Summary |
|------|------|---------|
| [`ApplicationBuilderExtensions`](tenantry-aspnetcore-extensions-applicationbuilderextensions.md) | class | Extension methods for adding Tenantry middleware to the request pipeline. |
| [`EndpointConventionBuilderExtensions`](tenantry-aspnetcore-extensions-endpointconventionbuilderextensions.md) | class | Extension methods for applying Tenantry endpoint metadata. |
| [`ServiceCollectionExtensions`](tenantry-aspnetcore-extensions-servicecollectionextensions.md) | class | Extension methods for registering Tenantry services. |

### `Tenantry.AspNetCore.Resolution`

| Type | Kind | Summary |
|------|------|---------|
| [`ClaimTenantResolver`](tenantry-aspnetcore-resolution-claimtenantresolver.md) | class | Resolves the tenant from a claim on the current request principal. |
| [`HeaderTenantResolver`](tenantry-aspnetcore-resolution-headertenantresolver.md) | class | Resolves the tenant from a request header (e.g. `X-Tenant-Id`). |
| [`ITenantResolver`](tenantry-aspnetcore-resolution-itenantresolver.md) | interface | Extracts a tenant identifier from an HTTP request. |
| [`QueryStringTenantResolver`](tenantry-aspnetcore-resolution-querystringtenantresolver.md) | class | Resolves the tenant from a query string parameter (e.g. `?tenantId=acme`). |
| [`RouteValueTenantResolver`](tenantry-aspnetcore-resolution-routevaluetenantresolver.md) | class | Resolves the tenant from a route value (e.g. `/api/{tenant}/resource`). |
| [`SubdomainTenantResolver`](tenantry-aspnetcore-resolution-subdomaintenantresolver.md) | class | Resolves the tenant from the first subdomain segment of the request host. For example, `acme.app.example.com` resolves to `acme`. |

## Tenantry.Core

### `Tenantry.Core`

| Type | Kind | Summary |
|------|------|---------|
| [`ITenantBuilder<TKey>`](tenantry-core-itenantbuilder.md) | interface | Minimal builder interface that captures `TKey` and exposes the service collection. Satellite packages (e.g. Tenantry.EfCore) add extension methods on this interface so users only specify TKey once in `AddTenantry`. |
| [`ITenantConnectionStringResolver<TKey>`](tenantry-core-itenantconnectionstringresolver.md) | interface | Returns tenants' connection strings, as configured by [`TenantConnectionStringOptions<TKey>`](tenantry-core-tenantconnectionstringoptions.md). |
| [`ITenantContext<TKey>`](tenantry-core-itenantcontext.md) | interface | Provides read-only access to the currently resolved tenant for the active request scope. Registered as a singleton backed by `AsyncLocal<T>` — the value is per-async-context (effectively per HTTP request) rather than per-instance. |
| [`ITenantDescriptor<TKey>`](tenantry-core-itenantdescriptor.md) | interface | Represents a resolved tenant. |
| [`ITenantScope<TKey>`](tenantry-core-itenantscope.md) | interface | Represents a scoped interface for managing tenant-specific context within the application. |
| [`ITenantScopeFactory<TKey>`](tenantry-core-itenantscopefactory.md) | interface | Opens tenant scopes for work that runs outside an HTTP request: hosted services, queue consumers, scheduled jobs and console tools. Each scope pairs a fresh dependency-injection scope with an active tenant, so scoped services such as a `DbContext` are created for that tenant and isolated to it. |
| [`ITenantScoped<TKey>`](tenantry-core-itenantscoped.md) | interface | Marker interface for objects that belong to a specific tenant. |
| [`ITenantServiceScope<TKey>`](tenantry-core-itenantservicescope.md) | interface | A dependency-injection scope with a tenant active, created by [`ITenantScopeFactory<TKey>`](tenantry-core-itenantscopefactory.md). |
| [`ITenantStore<TKey>`](tenantry-core-itenantstore.md) | interface | Persists and retrieves tenant definitions. |
| [`ITenantStoreAccessor<TKey>`](tenantry-core-itenantstoreaccessor.md) | interface | Reads tenants from the registered [`ITenantStore<TKey>`](tenantry-core-itenantstore.md) on behalf of singletons, such as hosted services, resolving the store from a fresh dependency-injection scope for each call. |
| [`MissingTenantBehavior`](tenantry-core-missingtenantbehavior.md) | enum | Policy for what happens when a tenant-scoped operation runs without a resolved tenant context. |
| [`TenantConnectionStringOptions<TKey>`](tenantry-core-tenantconnectionstringoptions.md) | class | How to find each tenant's connection string, for applications that give tenants their own database (or route them to different servers). Configure it with `UseConnectionStrings` and read connection strings through [`ITenantConnectionStringResolver<TKey>`](tenantry-core-itenantconnectionstringresolver.md). |
| [`TenantConnectionStringResolver<TKey>`](tenantry-core-tenantconnectionstringresolver.md) | class | The default [`ITenantConnectionStringResolver<TKey>`](tenantry-core-itenantconnectionstringresolver.md): calls the configured delegates on every resolution, without caching. |
| [`TenantDescriptor<TKey>`](tenantry-core-tenantdescriptor.md) | class | Default implementation of [`ITenantDescriptor<TKey>`](tenantry-core-itenantdescriptor.md). |
| [`TenantScoped<TKey>`](tenantry-core-tenantscoped.md) | class | Optional base class for tenant-owned objects. Implements [`ITenantScoped<TKey>`](tenantry-core-itenantscoped.md) for convenience. |

### `Tenantry.Core.Exceptions`

| Type | Kind | Summary |
|------|------|---------|
| [`TenantIsolationViolationException`](tenantry-core-exceptions-tenantisolationviolationexception.md) | class | Thrown when a cross-tenant data isolation violation is detected during a `SaveChanges` or `SaveChangesAsync` call, or when a bulk update would set `TenantId`. |
| [`TenantNotResolvedException`](tenantry-core-exceptions-tenantnotresolvedexception.md) | class | Thrown when a tenant could not be resolved from the current request context and the operation requires a resolved tenant. |

### `Tenantry.Core.Extensions`

| Type | Kind | Summary |
|------|------|---------|
| [`ConnectionStringExtensions`](tenantry-core-extensions-connectionstringextensions.md) | class | Registers per-tenant connection strings: [`TenantConnectionStringOptions<TKey>`](tenantry-core-tenantconnectionstringoptions.md) and [`ITenantConnectionStringResolver<TKey>`](tenantry-core-itenantconnectionstringresolver.md). |
| [`ServiceCollectionExtensions`](tenantry-core-extensions-servicecollectionextensions.md) | class | Extension methods for registering core Tenantry services. |

### `Tenantry.Core.Stores`

| Type | Kind | Summary |
|------|------|---------|
| [`InMemoryTenantStore<TKey>`](tenantry-core-stores-inmemorytenantstore.md) | class | An [`ITenantStore<TKey>`](tenantry-core-itenantstore.md) backed by an in-memory dictionary. |

## Tenantry.EfCore

### `Tenantry.EfCore`

| Type | Kind | Summary |
|------|------|---------|
| [`EfCoreIsolationOptions`](tenantry-efcore-efcoreisolationoptions.md) | class | Options for configuring EF Core tenant isolation registration. Passed to `builder.AddEfCoreIsolation(options => ...)`. |
| [`ITenantAwareDbContext<TKey>`](tenantry-efcore-itenantawaredbcontext.md) | interface | Marks a `DbContext` as tenant-aware, exposing the current tenant identifier for use in EF Core global query filters. |
| [`MultiTenantDbContext<TKey>`](tenantry-efcore-multitenantdbcontext.md) | class | Optional base `DbContext` that automatically applies tenant query filters in `OnModelCreating`. |

### `Tenantry.EfCore.Extensions`

| Type | Kind | Summary |
|------|------|---------|
| [`DbContextOptionsBuilderExtensions`](tenantry-efcore-extensions-dbcontextoptionsbuilderextensions.md) | class | Extension methods for wiring Tenantry into `DbContextOptionsBuilder`. |
| [`TenantBuilderEfCoreExtensions`](tenantry-efcore-extensions-tenantbuilderefcoreextensions.md) | class | Extension methods for configuring EF Core tenant isolation on [`ITenantBuilder<TKey>`](tenantry-core-itenantbuilder.md). |
| [`TenantDbContextPoolExtensions`](tenantry-efcore-extensions-tenantdbcontextpoolextensions.md) | class | DbContext pooling for applications that give each tenant its own database. |
| [`TenantModelBuilderExtensions`](tenantry-efcore-extensions-tenantmodelbuilderextensions.md) | class | Extension methods for `ModelBuilder` that apply tenant isolation to all entities implementing [`ITenantScoped<TKey>`](tenantry-core-itenantscoped.md). |
