# Calling other services

When one of your services calls another as a tenant, the called service needs to know which tenant. `Tenantry.Http`
sends the current tenant with an `HttpClient`'s or gRPC client's requests, in the `tenantry-tenant-id` header
(`TenantPropagation.HeaderName`), and `Tenantry.AspNetCore` reads it on the other side with
`ResolveFromPropagationHeader()`.

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

A gRPC client from `Grpc.Net.ClientFactory` runs on an `HttpClient`, so it takes the same call:

```csharp no-compile
builder.Services.AddGrpcClient<Inventory.InventoryClient>(o => o.Address = new Uri("https://inventory.internal"))
    .UseTenantry();
```

`UseTenantry()` without `AddHttpPropagation()` fails when the client is created, naming the call to add.

### Which requests carry it

- **A tenant is current:** the request carries its id, formatted with the invariant culture (`TenantIds.Format`).
  With no tenant, the request goes without the header, and the called service decides what that means, for example
  with `RequireTenant()`.
- **The caller has not set the header:** a header already on the request is left as it is.
- **The request is for the client's own service:** when the client has a base address, set in its configuration as
  above, only requests to that scheme, host and port carry the tenant; a request to an absolute address elsewhere does
  not. A client with no base address there carries it on every request: a gRPC client, whose address is the
  channel's, or a typed client that sets `BaseAddress` in its constructor.
- **A redirect keeps it.** `HttpClient` follows a redirect inside its primary handler with the request's headers, so
  a service that redirects to another passes the tenant id on. Turn redirects off for the client
  (`ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false })`) if a service
  you call may redirect where the id should not go.

`UseTenantry()` refuses `ConfigureHttpClientDefaults`, which configures every client in the application, third-party
SDKs' included: tenant ids go only to the services you name.

A header carries printable ASCII, and the receiving side trims it, so an id must be printable ASCII with no space at
either end (a GUID, a number or a slug). A request as a tenant whose id is not throws `InvalidOperationException`,
rather than sending something the other side would read as another id.

## Receiving the tenant

The called service resolves the tenant from the header with `ResolveFromPropagationHeader()`:

```csharp
builder.Services.AddTenantry<Guid>(tenant => tenant
    .ResolveFromPropagationHeader()
    .UseStore<EfCoreTenantStore>());
```

The header holds a tenant id, so the resolver reads it with `TenantIds.TryParse` and looks the tenant up with the
store's `GetTenantAsync`, not `FindByIdentifierAsync`: a store whose identifiers are slugs still finds the tenant. A
value that is not a tenant id, or is an id reserved for "no tenant", finds no tenant.

Resolvers run in the order they are added, and the first that finds a value wins. A service that serves both users
and other services resolves its users' requests first and takes the header only when nothing else names a tenant. Its
users can set the header too, so an access validator accepts it only from callers that authenticated as one of your
services (here, with a token carrying a `scope` claim your identity provider gives only to services):

```csharp
using Tenantry;

builder.Services.AddTenantry<Guid>(tenant => tenant
    .ResolveFromSubdomain(o => o.BaseDomains.Add("example.com"))
    .ResolveFromPropagationHeader()
    .UseStore<EfCoreTenantStore>()
    .ValidateTenantAccess((http, _) =>
        !http.Request.Headers.ContainsKey(TenantPropagation.HeaderName) || http.User.HasClaim("scope", "internal")));
```

### A header is a claim, not proof

Any caller that reaches the service can set the header. Accept it only from callers you authenticate (a bearer token
from your identity provider, mutual TLS), and check that the caller may act for the tenant with an
[access validator](access-control.md). A service that only other services call should not be reachable from outside
at all. Where the calling service already sends a token that names the tenant, `ResolveFromClaim` reads it from the
token instead, and the header is not needed.

## Jobs and messages

Tenantry.Pro's Hangfire, MassTransit, Quartz.NET and Rebus integrations carry the tenant under the same name, as a
job parameter or message header, so a job that calls a service with `UseTenantry()` calls it as the job's tenant.

## See also

- [Tenant resolution](tenant-resolution.md) — the other resolvers, and their order
- [Access control](access-control.md) — access validators
- [Diagnostics](diagnostics.md) — the `tenant.id` tag and the `TenantId` log scope
