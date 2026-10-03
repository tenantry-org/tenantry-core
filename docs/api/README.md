# API reference

Every public type in the packages, generated from their XML documentation comments. The guides explain how
the pieces fit together; this reference is for the details of each type and member.

## Tenantry.AspNetCore

### `Microsoft.AspNetCore.Builder`

| Type | Kind | Summary |
|------|------|---------|
| [`TenantryApplicationBuilderExtensions`](microsoft-aspnetcore-builder-tenantryapplicationbuilderextensions.md) | class | Adds Tenantry's tenant resolution to the request pipeline. |
| [`TenantryEndpointConventionBuilderExtensions`](microsoft-aspnetcore-builder-tenantryendpointconventionbuilderextensions.md) | class | Extension methods for applying Tenantry endpoint metadata. |

### `Microsoft.Extensions.DependencyInjection`

| Type | Kind | Summary |
|------|------|---------|
| [`TenantryAspNetCoreTenantBuilderExtensions`](microsoft-extensions-dependencyinjection-tenantryaspnetcoretenantbuilderextensions.md) | class | Tenantry's ASP.NET Core features on [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): how a request is resolved to a tenant, whether endpoints need one, and who may use it. `app.UseTenantry()` applies them to requests. |

### `Tenantry.AspNetCore`

| Type | Kind | Summary |
|------|------|---------|
| [`AllowMissingTenantAttribute`](tenantry-aspnetcore-allowmissingtenantattribute.md) | class | Allows an endpoint or controller action to execute without a resolved tenant. |
| [`ClaimTenantResolver`](tenantry-aspnetcore-claimtenantresolver.md) | class | Resolves the tenant from a claim on the current request principal. |
| [`HeaderTenantResolver`](tenantry-aspnetcore-headertenantresolver.md) | class | Resolves the tenant from a request header (e.g. `X-Tenant-Id`). |
| [`HostTenantResolver`](tenantry-aspnetcore-hosttenantresolver.md) | class | Resolves the tenant from the request's host name, for tenants that bring their own domain: a request to `app.acme.com` has the identifier `app.acme.com`, which your tenant store's [`ITenantStore<TKey>.FindByIdentifierAsync`](tenantry-itenantstore.md) maps to a tenant. |
| [`HostTenantResolverOptions`](tenantry-aspnetcore-hosttenantresolveroptions.md) | class | Options for [`HostTenantResolver`](tenantry-aspnetcore-hosttenantresolver.md), set with `tenant.ResolveFromHost(o => …)`. |
| [`ITenantAccessValidator<TKey>`](tenantry-aspnetcore-itenantaccessvalidator.md) | interface | Decides whether a request may use the tenant it names. Register one with `tenant.ValidateTenantAccess<TValidator>()`: it is created in each request's scope, so it can depend on scoped services such as a `DbContext`. |
| [`ITenantResolver`](tenantry-aspnetcore-itenantresolver.md) | interface | Reads a tenant identifier from an HTTP request: the tenant's id, or a name the tenant store maps to a tenant, such as a subdomain or a host name (see [`ITenantStore<TKey>.FindByIdentifierAsync`](tenantry-itenantstore.md)). |
| [`QueryStringTenantResolver`](tenantry-aspnetcore-querystringtenantresolver.md) | class | Resolves the tenant from a query string parameter (e.g. `?tenantId=acme`). |
| [`RequireTenantAttribute`](tenantry-aspnetcore-requiretenantattribute.md) | class | Requires Tenantry to resolve a tenant before the endpoint or controller action executes. |
| [`RouteValueTenantResolver`](tenantry-aspnetcore-routevaluetenantresolver.md) | class | Resolves the tenant from a route value (e.g. `/api/{tenant}/resource`). |
| [`SubdomainTenantResolver`](tenantry-aspnetcore-subdomaintenantresolver.md) | class | Resolves the tenant from the subdomain of the request host. For example, `acme.app.example.com` resolves to `acme`. |
| [`SubdomainTenantResolverOptions`](tenantry-aspnetcore-subdomaintenantresolveroptions.md) | class | Options for [`SubdomainTenantResolver`](tenantry-aspnetcore-subdomaintenantresolver.md), set with `tenant.ResolveFromSubdomain(o => …)`. |
| [`TenantRejectedContext<TKey>`](tenantry-aspnetcore-tenantrejectedcontext.md) | class | A request that `app.UseTenantry()` rejects, passed to [`TenantResolutionOptions<TKey>.OnRejected`](tenantry-aspnetcore-tenantresolutionoptions.md). |
| [`TenantRejectionReason`](tenantry-aspnetcore-tenantrejectionreason.md) | enum | Why `app.UseTenantry()` rejects a request to an endpoint that needs a tenant. |
| [`TenantResolutionOptions<TKey>`](tenantry-aspnetcore-tenantresolutionoptions.md) | class | How `app.UseTenantry()` treats requests: whether they need a tenant, the status code of each rejection, and the events it raises. Configure it with `tenant.ConfigureResolution(o => …)` or `tenant.RequireTenantByDefault()`. |
| [`TenantResolvedContext<TKey>`](tenantry-aspnetcore-tenantresolvedcontext.md) | class | The request whose tenant `app.UseTenantry()` made current, passed to [`TenantResolutionOptions<TKey>.OnResolved`](tenantry-aspnetcore-tenantresolutionoptions.md). |
| [`TenantryAspNetCoreTelemetry`](tenantry-aspnetcore-tenantryaspnetcoretelemetry.md) | class | The names `app.UseTenantry()` records requests under, for OpenTelemetry's `AddSource` and `AddMeter`, and for log filters. The request's tenant itself goes under [`TenantTelemetry`](tenantry-tenanttelemetry.md)'s names. |

## Tenantry.Caching

### `Microsoft.Extensions.DependencyInjection`

| Type | Kind | Summary |
|------|------|---------|
| [`TenantryCachingTenantBuilderExtensions`](microsoft-extensions-dependencyinjection-tenantrycachingtenantbuilderextensions.md) | class | Keeps cached data per tenant. |

### `Tenantry.Caching`

| Type | Kind | Summary |
|------|------|---------|
| [`ITenantDistributedCache`](tenantry-caching-itenantdistributedcache.md) | interface | The registered `IDistributedCache`, with every key under the current tenant's prefix, for code that uses `IDistributedCache` directly and keeps data per tenant. Without a tenant, every call throws [`TenantNotResolvedException`](tenantry-tenantnotresolvedexception.md). |
| [`SharedHybridCache`](tenantry-caching-sharedhybridcache.md) | class | The `HybridCache` for entries every tenant shares (exchange rates, reference data), where `IsolateCaches()` makes the injected `HybridCache` keep entries per tenant. |

## Tenantry.Core

### `Microsoft.Extensions.DependencyInjection`

| Type | Kind | Summary |
|------|------|---------|
| [`TenantryServiceCollectionExtensions`](microsoft-extensions-dependencyinjection-tenantryservicecollectionextensions.md) | class | Registers Tenantry. |
| [`TenantryTenantBuilderExtensions`](microsoft-extensions-dependencyinjection-tenantrytenantbuilderextensions.md) | class | Tenantry's core features on [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): the tenant store, its cache and per-tenant connection strings. |

### `Tenantry`

| Type | Kind | Summary |
|------|------|---------|
| [`CurrentTenantConnectionString<TKey>`](tenantry-currenttenantconnectionstring.md) | class | Returns the current tenant's connection string, through the registered [`ITenantConnectionStringProvider<TKey>`](tenantry-itenantconnectionstringprovider.md). |
| [`ITenantActivity<TKey>`](tenantry-itenantactivity.md) | interface | Whether a tenant may have work run for it, as the registered [`ITenantActivityValidator<TKey>`](tenantry-itenantactivityvalidator.md)s decide. |
| [`ITenantActivityValidator<TKey>`](tenantry-itenantactivityvalidator.md) | interface | Decides whether a tenant may have work run for it: requests, background work, jobs and messages. |
| [`ITenantBuilder`](tenantry-itenantbuilder.md) | interface | The builder `AddTenantry` passes to its configuration callback, without the tenant key type. Features that take a type parameter of their own register through [`ITenantBuilder.Add`](tenantry-itenantbuilder.md), so their callers never repeat the key type. |
| [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md) | interface | The builder `AddTenantry<TKey>` passes to its configuration callback. |
| [`ITenantConnectionStringProvider<TKey>`](tenantry-itenantconnectionstringprovider.md) | interface | Returns a tenant's connection string, as configured by [`TenantConnectionStringOptions<TKey>`](tenantry-tenantconnectionstringoptions.md). |
| [`ITenantContext<TKey>`](tenantry-itenantcontext.md) | interface | The current tenant, for a request or an [`ITenantScopeFactory<TKey>`](tenantry-itenantscopefactory.md) scope. A singleton over an `AsyncLocal<T>`: the value belongs to the async flow, not to the instance. |
| [`ITenantContextSetter<TKey>`](tenantry-itenantcontextsetter.md) | interface | Makes a tenant current for the calling code, for code that has already found the tenant and needs no new dependency-injection scope. Most code uses [`ITenantScopeFactory<TKey>`](tenantry-itenantscopefactory.md) instead, which also creates a scope for the tenant's services. |
| [`ITenantDescriptor`](tenantry-itenantdescriptor.md) | interface | A tenant, without its identifier type: the base of [`ITenantDescriptor<TKey>`](tenantry-itenantdescriptor-1.md), for code that does not need the tenant's id, such as [`TenantDescriptorExtensions.As<TTenant>`](tenantry-tenantdescriptorextensions.md). |
| [`ITenantDescriptor<TKey>`](tenantry-itenantdescriptor-1.md) | interface | Represents a resolved tenant. |
| [`ITenantEntity<TKey>`](tenantry-itenantentity.md) | interface | Marks an entity that belongs to a tenant. |
| [`ITenantInvalidationHandler<TKey>`](tenantry-itenantinvalidationhandler.md) | interface | Clears data kept per tenant when the tenant changes. Every registered handler runs on [`ITenantInvalidator<TKey>`](tenantry-itenantinvalidator.md) and [`ITenantStoreCache<TKey>`](tenantry-itenantstorecache.md) invalidation, with or without `CacheTenants`. Tenantry.Caching, `IsolateOutputCache()` and Tenantry.Options register their own. |
| [`ITenantInvalidator<TKey>`](tenantry-itenantinvalidator.md) | interface | Clears everything Tenantry keeps for a tenant when the tenant changes: its cached copy (with `CacheTenants`) and what each [`ITenantInvalidationHandler<TKey>`](tenantry-itenantinvalidationhandler.md) keeps, such as Tenantry.Caching's entries, cached responses and Tenantry.Options' values. |
| [`ITenantKeyType`](tenantry-itenantkeytype.md) | interface | The tenant key type the application registered Tenantry with, for code that has only a service provider or a service collection, such as a health check registration or a host extension, so its callers never repeat the key type. `AddTenantry` registers it as a singleton; `services.FindTenantKeyType()` reads it while services are being registered. |
| [`ITenantKeyTypeVisitor<TResult>`](tenantry-itenantkeytypevisitor.md) | interface | Code that needs the tenant key type, given it by [`ITenantKeyType`](tenantry-itenantkeytype.md). |
| [`ITenantLookup<TKey>`](tenantry-itenantlookup.md) | interface | Reads tenants from the registered [`ITenantStore<TKey>`](tenantry-itenantstore.md) on behalf of singletons, such as hosted services, resolving the store from a fresh dependency-injection scope for each call. |
| [`ITenantRegistration`](tenantry-itenantregistration.md) | interface | A registration that needs the tenant key type, added through [`ITenantBuilder.Add`](tenantry-itenantbuilder.md). Packages use it for builder methods that take a type parameter of their own, such as a `DbContext` type. |
| [`ITenantScope<TKey>`](tenantry-itenantscope.md) | interface | A dependency-injection scope with a tenant current, created by [`ITenantScopeFactory<TKey>`](tenantry-itenantscopefactory.md). |
| [`ITenantScopeFactory<TKey>`](tenantry-itenantscopefactory.md) | interface | Opens tenant scopes for work that runs outside an HTTP request: hosted services, queue consumers, scheduled jobs and console tools. Each scope pairs a fresh dependency-injection scope with an active tenant, so scoped services such as a `DbContext` are created for that tenant and isolated to it. |
| [`ITenantStore<TKey>`](tenantry-itenantstore.md) | interface | Persists and retrieves tenant definitions. |
| [`ITenantStoreCache<TKey>`](tenantry-itenantstorecache.md) | interface | Removes cached tenants and runs every [`ITenantInvalidationHandler<TKey>`](tenantry-itenantinvalidationhandler.md), waiting for them. Call it when a tenant changes or is removed. In asynchronous code, [`ITenantInvalidator<TKey>`](tenantry-itenantinvalidator.md) does the same without blocking. |
| [`InMemoryTenantStore<TKey>`](tenantry-inmemorytenantstore.md) | class | An [`ITenantStore<TKey>`](tenantry-itenantstore.md) backed by an in-memory dictionary. |
| [`TenantConnectionStringOptions<TKey>`](tenantry-tenantconnectionstringoptions.md) | class | How to find each tenant's connection string, for applications that give tenants their own database (or route them to different servers). Configure it with `UseConnectionStrings` and read connection strings through [`ITenantConnectionStringProvider<TKey>`](tenantry-itenantconnectionstringprovider.md), or the current tenant's through [`CurrentTenantConnectionString<TKey>`](tenantry-currenttenantconnectionstring.md). |
| [`TenantConnectionStringProvider<TKey>`](tenantry-tenantconnectionstringprovider.md) | class | The default [`ITenantConnectionStringProvider<TKey>`](tenantry-itenantconnectionstringprovider.md): calls the configured delegates on every call, without caching. |
| [`TenantDescriptor<TKey>`](tenantry-tenantdescriptor.md) | class | Default implementation of [`ITenantDescriptor<TKey>`](tenantry-itenantdescriptor-1.md). |
| [`TenantDescriptorExtensions`](tenantry-tenantdescriptorextensions.md) | class | Reads a tenant as the application's own tenant type. |
| [`TenantEntity<TKey>`](tenantry-tenantentity.md) | class | Optional base class for tenant-owned entities, implementing [`ITenantEntity<TKey>`](tenantry-itenantentity.md). |
| [`TenantIds`](tenantry-tenantids.md) | class | Tenant ids as text, and the ids Tenantry reserves for "no tenant". Tenantry formats and parses tenant ids this way wherever they leave or enter the process: in log scopes and traces, in the headers Tenantry.Http and Tenantry.Pro's jobs and messages carry, and in the identifiers [`ITenantStore<TKey>.FindByIdentifierAsync`](tenantry-itenantstore.md) reads by default. |
| [`TenantInactiveException`](tenantry-tenantinactiveexception.md) | class | Thrown when work is to run for a tenant that an [`ITenantActivityValidator<TKey>`](tenantry-itenantactivityvalidator.md) refuses, such as a suspended tenant, for example by [`ITenantScopeFactory<TKey>.RunInScopeAsync`](tenantry-itenantscopefactory.md). |
| [`TenantNotFoundException`](tenantry-tenantnotfoundexception.md) | class | Thrown when a tenant is looked up by its id and the tenant store has no tenant with that id, for example by [`ITenantScopeFactory<TKey>.RunInScopeAsync`](tenantry-itenantscopefactory.md). |
| [`TenantNotResolvedException`](tenantry-tenantnotresolvedexception.md) | class | Thrown when an operation needs a current tenant and none is current, or when the tenant it names does not exist ([`TenantNotFoundException`](tenantry-tenantnotfoundexception.md)). |
| [`TenantPropagation`](tenantry-tenantpropagation.md) | class | How Tenantry carries a tenant from one process to another: Tenantry.Http's outgoing requests, Tenantry.AspNetCore's `ResolveFromPropagationHeader(...)` on the receiving side, and Tenantry.Pro's Hangfire, MassTransit, Quartz.NET and Rebus integrations. |
| [`TenantStoreCacheOptions`](tenantry-tenantstorecacheoptions.md) | class | How Tenantry caches the tenants it reads from the tenant store. Set with `tenant.CacheTenants(o => …)`. |
| [`TenantTelemetry`](tenantry-tenanttelemetry.md) | class | The names Tenantry records a tenant under in traces and logs: Tenantry.AspNetCore's `app.UseTenantry()` for a request, and Tenantry.Pro for jobs, messages and background work. |

## Tenantry.EfCore

### `Microsoft.EntityFrameworkCore`

| Type | Kind | Summary |
|------|------|---------|
| [`TenantryDbContextOptionsBuilderExtensions`](microsoft-entityframeworkcore-tenantrydbcontextoptionsbuilderextensions.md) | class | Extension methods for wiring Tenantry into `DbContextOptionsBuilder`. |
| [`TenantryEntityTypeBuilderExtensions`](microsoft-entityframeworkcore-tenantryentitytypebuilderextensions.md) | class | Marks entity types in the model for Tenantry. |

### `Microsoft.Extensions.DependencyInjection`

| Type | Kind | Summary |
|------|------|---------|
| [`TenantryEfCoreTenantBuilderExtensions`](microsoft-extensions-dependencyinjection-tenantryefcoretenantbuilderextensions.md) | class | Extension methods for configuring EF Core tenant isolation on [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md). |

### `Tenantry.EfCore`

| Type | Kind | Summary |
|------|------|---------|
| [`EfCoreIsolationOptions`](tenantry-efcore-efcoreisolationoptions.md) | class | Options for EF Core tenant isolation: the application's, set with `tenant.ConfigureEfCoreIsolation(options => …)`, or one context's, set with `options.UseTenantry(o => …)`. |
| [`ITenantDbContextOptionsContributor`](tenantry-efcore-itenantdbcontextoptionscontributor.md) | interface | Adds to the options of every `DbContext` that uses `UseTenantry()`. Register implementations in the application's service collection, as singletons; `UseTenantry()` applies each of them. |
| [`ITenantModelContributor`](tenantry-efcore-itenantmodelcontributor.md) | interface | Adds to the model of every `DbContext` that uses `UseTenantry()`. |
| [`MissingTenantBehavior`](tenantry-efcore-missingtenantbehavior.md) | enum | What `SaveChanges` does when it writes tenant-owned entities and no tenant is current. Set with [`EfCoreIsolationOptions.OnMissingTenant`](tenantry-efcore-efcoreisolationoptions.md). |
| [`SaveWithoutTransactionBehavior`](tenantry-efcore-savewithouttransactionbehavior.md) | enum | What `SaveChanges` does, with `Database.AutoTransactionBehavior` set to `Never` and no transaction, when the tenant check of some rows it writes is another of its statements: owned rows in a table of their own, checked by their owner's statement, and the rows of an entity mapped to more than one table (table-per-type, entity splitting), checked in the table with `TenantId`. Without a transaction, a statement EF Core sends beside a check that fails would stay written. Set with [`EfCoreIsolationOptions.OnSaveWithoutTransaction`](tenantry-efcore-efcoreisolationoptions.md). |
| [`SharedAcrossTenantsAttribute`](tenantry-efcore-sharedacrosstenantsattribute.md) | class | Marks an entity type whose rows every tenant shares, such as a country list or the tenant table itself, so [`TenantModel.FindUnisolatedEntityTypes`](tenantry-efcore-tenantmodel.md) does not report it. |
| [`TenantContextGuard`](tenantry-efcore-tenantcontextguard.md) | class | An interceptor that checks a context before it opens a connection, before every command it runs and before `SaveChanges`, so a context that would reach the wrong tenant's data throws instead. |
| [`TenantIsolationViolationException`](tenantry-efcore-tenantisolationviolationexception.md) | class | Thrown when EF Core would read or write across tenants. [`TenantIsolationViolationException.Kind`](tenantry-efcore-tenantisolationviolationexception.md) says which check failed. |
| [`TenantIsolationViolationKind`](tenantry-efcore-tenantisolationviolationkind.md) | enum | Which isolation check threw a [`TenantIsolationViolationException`](tenantry-efcore-tenantisolationviolationexception.md). |
| [`TenantModel`](tenantry-efcore-tenantmodel.md) | class | Reads which entity types of an EF Core model Tenantry isolates, for packages and tests that build on it. |
| [`TenantryQueryFilters`](tenantry-efcore-tenantryqueryfilters.md) | class | The names of the query filters Tenantry adds or names on EF Core 10 and later. |

## Tenantry.Http

### `Microsoft.Extensions.DependencyInjection`

| Type | Kind | Summary |
|------|------|---------|
| [`TenantryHttpClientBuilderExtensions`](microsoft-extensions-dependencyinjection-tenantryhttpclientbuilderextensions.md) | class | Sends the current tenant with an HTTP or gRPC client's requests. |
| [`TenantryHttpTenantBuilderExtensions`](microsoft-extensions-dependencyinjection-tenantryhttptenantbuilderextensions.md) | class | Sends the current tenant to the services an application calls over HTTP or gRPC. |

## Tenantry.Options

### `Microsoft.Extensions.DependencyInjection`

| Type | Kind | Summary |
|------|------|---------|
| [`TenantryOptionsTenantBuilderExtensions`](microsoft-extensions-dependencyinjection-tenantryoptionstenantbuilderextensions.md) | class | Options with values per tenant. |

### `Tenantry.Options`

| Type | Kind | Summary |
|------|------|---------|
| [`TenantOptionsBuilder<TKey>`](tenantry-options-tenantoptionsbuilder.md) | class | Sets options per tenant, in `tenant.ConfigurePerTenant(...)`: each `Configure` or `ConfigureAll` names an options type and what differs for each tenant. `IOptionsSnapshot<TOptions>` and `IOptionsMonitor<TOptions>` then give the current tenant's value, built from the ordinary configuration (every `Configure`), then these steps with the tenant. Without a tenant they give the ordinary value. `IOptions<TOptions>` always gives the ordinary value. |
