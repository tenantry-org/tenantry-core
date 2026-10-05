# Calling other services

When one of your services calls another as a tenant, `Tenantry.Http` sends the current tenant with an `HttpClient`'s or
gRPC client's requests, in the `tenantry-tenant-id` header (`TenantPropagation.HeaderName`), and `Tenantry.AspNetCore`
reads it on the other side with `ResolveFromPropagationHeader`.

The header name is fixed. To call a service that expects another header, set it from the current tenant in a
`DelegatingHandler` of your own. To accept another header, use `ResolveFromHeader("X-Tenant-Id")`, which, unlike
`ResolveFromPropagationHeader`, accepts it from any caller and looks it up as an identifier: add an
[access validator](access-control.md).

```bash
dotnet add package Tenantry.Http
```

## Sending the tenant

Add `AddHttpPropagation()` in `AddTenantry`, then `UseTenantry()` on each client of your own services:

```csharp
builder.Services.AddTenantry<Guid>(tenant => tenant
    .ResolveFromSubdomain(o => o.BaseDomains.Add("example.com"))
    .UseStore<EfCoreTenantStore>()
    .AddHttpPropagation());

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
from `Grpc.Net.ClientFactory` keeps its address in its own options, which Tenantry cannot read, and a typed client may
set `BaseAddress` in its constructor, after the handlers are built. Pass such a client's address to `UseTenantry`:

```csharp no-compile
var inventory = new Uri("https://inventory.internal");
builder.Services.AddGrpcClient<Inventory.InventoryClient>(o => o.Address = inventory)
    .UseTenantry(inventory);
```

A client created with neither, or without `AddHttpPropagation()`, fails when it is created, saying what to add.

### Which requests carry it

- While a tenant is current, the request carries the tenant's id, formatted with the invariant culture
  (`TenantIds.Format`). With no tenant, the request goes without the header, and the called service decides what that
  means, for example with `RequireTenant()`.
- A request that already carries the header with another tenant's id, while a tenant is current, throws
  `InvalidOperationException`, which catches a header forwarded from the incoming request or set in
  `DefaultRequestHeaders`. To call as another tenant, make it current with `ITenantContextSetter.MakeCurrent`. With
  no current tenant, a header you set is sent as it is.
- Only requests to the scheme, host and port of the address passed to `UseTenantry`, or else of the registration's
  `BaseAddress`, get the header. A request with an absolute address elsewhere goes without it.
- `HttpClient` follows a redirect inside its primary handler with the request's headers, so a service that redirects
  to another passes the tenant id on. Turn redirects off for the client
  (`ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false })`) if a service you
  call may redirect where the id should not go.

`UseTenantry()` refuses `ConfigureHttpClientDefaults`, which configures every client in the application, third-party
SDKs' included: tenant ids go only to the services you name.

Tenant ids must be printable ASCII with no leading or trailing space (a GUID, number or slug); sending any other id
throws `InvalidOperationException`.

## Receiving the tenant

The called service resolves the tenant from the header with `ResolveFromPropagationHeader`. Any caller that reaches
the service can set the header, so the resolver takes a check of the caller and reads the header only when it passes.
Here the caller must have authenticated with a token carrying an `internal` scope, which your identity provider gives
only to your services:

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

- The check reads the authenticated user, so `app.UseTenantResolution()` stops before this resolver and
  `app.UseTenantry()` runs it after `app.UseAuthentication()`: a tenant from the header is not known while
  authentication runs, and its schemes use their default settings.
- When the check fails, the header is ignored and the next resolver runs. A caller with no token is not trusted, so
  placing `app.UseTenantry()` before `app.UseAuthentication()` makes every header ignored rather than accepted.
- The resolver reads the value as a tenant id, with `TenantIds.TryParse`, and looks the tenant up with the store's
  `GetTenantAsync`, not `FindByIdentifierAsync`, so a store whose identifiers are slugs still finds the tenant. A value
  that is not a tenant id, or is an id reserved for "no tenant", finds no tenant.

Resolvers run in the order they are added, and the first that finds a value wins. A service that serves both users
and other services resolves its users' requests first and takes the header only when nothing else names a tenant:

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

## Jobs and messages

Tenantry.Pro's Hangfire, MassTransit, Quartz.NET and Rebus integrations carry the tenant under the same name, as a
job parameter or message header, so a job that calls a service with `UseTenantry()` calls it as the job's tenant.

## See also

- [Tenant resolution](tenant-resolution.md): the other resolvers, and their order
- [Access control](access-control.md): access validators
- [Diagnostics](diagnostics.md): the `tenant.id` tag and the `TenantId` log scope
