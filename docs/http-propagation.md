# Calling other services

When one of your services calls another as a tenant, `Tenantry.Http` sends the current tenant with an `HttpClient`'s or
gRPC client's requests, in the `tenantry-tenant-id` header (`TenantPropagation.HeaderName`). `Tenantry.AspNetCore`
reads it on the other side with `ResolveFromPropagationHeader`.

```bash
dotnet add package Tenantry.Http
```

## Sending the tenant

Add `UseTenantry()` to each client of your own services:

```csharp
builder.Services.AddTenantry<Guid>(tenant => tenant
    .ResolveFromSubdomain(o => o.BaseDomains.Add("example.com"))
    .UseStore<EfCoreTenantStore>());

builder.Services.AddHttpClient<BillingClient>(c => c.BaseAddress = new Uri("https://billing.internal"))
    .UseTenantry();
```

A typed client then calls as whoever the current tenant is, in a request, a tenant scope or a job:

```csharp
public sealed class BillingClient(HttpClient http)
{
    public Task<string> GetBalanceAsync(CancellationToken ct) => http.GetStringAsync("/balance", ct);
}
```

The tenant goes only to the service at the client's `BaseAddress`, so the registration must set one. A gRPC client
from `Grpc.Net.ClientFactory`, or a typed client that sets `BaseAddress` in its constructor, passes the address to
`UseTenantry` instead
([`UseTenantry`](api/microsoft-extensions-dependencyinjection-tenantryhttpclientbuilderextensions.md)):

```csharp no-compile
var inventory = new Uri("https://inventory.internal");
builder.Services.AddGrpcClient<Inventory.InventoryClient>(o => o.Address = inventory)
    .UseTenantry(inventory);
```

A client with neither stops the host as it starts (`InvalidOperationException`), saying what to add. In a service
provider built without a host, the client fails when it is created.

### Which requests carry it

- While a tenant is current, the request carries its id, formatted with the invariant culture (`TenantIds.Format`).
  With none, it carries no header, and the called service decides what that means, for example with
  `RequireTenant()`.
- A request that already carries the header throws `InvalidOperationException` unless it names the current tenant, so
  it always throws with no tenant current. The header may be forwarded from the incoming request or set in
  `DefaultRequestHeaders`. To call as another tenant, make it current with `MakeCurrent` or `RunInScopeAsync`.
- Call `UseTenantry()` after `AddHeaderPropagation()` and after any handler that sets headers: a handler added after it
  sets headers Tenantry does not see.
- Only requests to the scheme, host and port of the address passed to `UseTenantry`, or else of the registration's
  `BaseAddress`, get the header. A request with an absolute address elsewhere goes without it.
- A redirect keeps the header: `HttpClient` follows it inside its primary handler with the request's headers, so a
  service that redirects to another passes the tenant id on. If a service you call may redirect where the id should
  not go, turn redirects off for the client
  (`ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false })`).
- Tenant ids must be printable ASCII with no leading or trailing space (a GUID, number or slug). Sending any other id
  throws `InvalidOperationException`.

`UseTenantry()` refuses `ConfigureHttpClientDefaults`, which configures every client in the application, third-party
SDKs' included: tenant ids go only to the services you name.

## Receiving the tenant

Any caller that reaches the service can set the header, so `ResolveFromPropagationHeader` takes a check of the caller
and reads the header only when it passes. Here the caller must have authenticated with a token carrying an `internal`
scope, which your identity provider gives only to your services:

```csharp
using System.Security.Claims;
using Tenantry;

builder.Services.AddTenantry<Guid>(tenant => tenant
    .ResolveFromPropagationHeader(http => HasScope(http.User, "internal"))
    .UseStore<EfCoreTenantStore>());

// An OAuth scope claim is usually one space-separated value ("internal orders.read"); some providers send one claim
// per scope. This reads both.
static bool HasScope(ClaimsPrincipal user, string scope) =>
    user.FindAll("scope").SelectMany(c => c.Value.Split(' ')).Contains(scope);
```

The claim's type and shape depend on your identity provider: Microsoft Entra ID puts scopes in `scp`, and a
client-credentials token may be easier to recognise by its `client_id` or `azp` claim. Mutual TLS works too: check
`http.Connection.ClientCertificate`.

- When the check fails, the header is ignored and the next resolver runs.
- The check reads the authenticated user, so the resolver runs after `app.UseAuthentication()`
  ([details](#the-check-and-authentication)).
- The value is read as a tenant id and looked up with the store's `GetTenantAsync`, so a store whose identifiers are
  slugs still finds the tenant
  ([`ResolveFromPropagationHeader`](api/microsoft-extensions-dependencyinjection-tenantryaspnetcoretenantbuilderextensions.md)).

Resolvers run in the order they are added, and the first that finds a value wins
([Resolver ordering](tenant-resolution.md#resolver-ordering-and-fallback)). A service that serves both users and other
services resolves its users' requests first and takes the header only when nothing else names a tenant:

```csharp
using System.Security.Claims;
using Tenantry;

builder.Services.AddTenantry<Guid>(tenant => tenant
    .ResolveFromSubdomain(o => o.BaseDomains.Add("example.com"))
    .ResolveFromPropagationHeader(http => HasScope(http.User, "internal"))
    .UseStore<EfCoreTenantStore>());

static bool HasScope(ClaimsPrincipal user, string scope) =>
    user.FindAll("scope").SelectMany(c => c.Value.Split(' ')).Contains(scope);
```

### A trusted caller can name any tenant

Where a calling service should act only for some tenants, check that with an [access validator](access-control.md).
A service that only other services call should not be reachable from outside at all. Where the calling service
already sends a token that names the tenant, `ResolveFromClaim` reads it from the token instead, and the header is not
needed.

## Another header

The header name is fixed. To call a service that expects another header, set it from the current tenant in a
`DelegatingHandler` of your own. To accept another header, use `ResolveFromHeader("X-Tenant-Id")`. Unlike
`ResolveFromPropagationHeader`, it accepts the header from any caller and looks it up as an identifier, so add an
[access validator](access-control.md).

## Jobs and messages

Tenantry.Pro's Hangfire, MassTransit, Quartz.NET and Rebus integrations carry the tenant under the same name, as a
job parameter or message header. A job that calls a service with `UseTenantry()` calls it as the job's tenant.

## Details

### The check and authentication

`app.UseTenantResolution()` stops before this resolver, and `app.UseTenantry()` runs it after
`app.UseAuthentication()` ([How the two steps work](authentication-per-tenant.md#how-the-two-steps-work)). So a tenant
from the header is not known while authentication runs, and its schemes use their default settings. A caller with no
token is not trusted, so placing `app.UseTenantry()` before `app.UseAuthentication()` makes every header ignored rather
than accepted.

## See also

- [Tenant resolution](tenant-resolution.md): the other resolvers, and their order
- [Access control](access-control.md): access validators
- [Diagnostics](diagnostics.md): the `tenant.id` tag and the `TenantId` log scope
