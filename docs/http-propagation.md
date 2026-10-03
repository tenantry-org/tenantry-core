# Calling other services

When one of your services calls another as a tenant, the called service needs to know which tenant. `Tenantry.Http`
sends the current tenant with an `HttpClient`'s or gRPC client's requests, in the `tenantry-tenant-id` header
(`TenantPropagation.HeaderName`), and `Tenantry.AspNetCore` reads it on the other side with
`ResolveFromPropagationHeader`.

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
- **A header naming another tenant is refused.** A request that already carries the header with another tenant's
  id, while a tenant is current, throws `InvalidOperationException`. That catches a header forwarded from the incoming
  request or set in `DefaultRequestHeaders`. To call as another tenant, make it current with `ITenantContextSetter.Use`.
  With no current tenant, a header you set is sent as it is.
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

The called service resolves the tenant from the header with `ResolveFromPropagationHeader`. Any caller that reaches
the service can set the header, so it takes a check of the caller, and reads the header only when the check passes.
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

- **Checked after authentication.** The check reads the authenticated user, so `app.UseTenantResolution()` stops
  before this resolver and `app.UseTenantry()` runs it after `app.UseAuthentication()`. A tenant from the header is
  therefore not known while authentication runs, so its schemes use their default settings.
- **Ignored from other callers.** When the check fails, the header is ignored and the next resolver runs. A caller
  with no token is not trusted, so placing `app.UseTenantry()` before `app.UseAuthentication()` makes every header
  ignored rather than accepted.
- **Read as a tenant id.** The resolver reads the value with `TenantIds.TryParse` and looks the tenant up with the
  store's `GetTenantAsync`, not `FindByIdentifierAsync`, so a store whose identifiers are slugs still finds the
  tenant. A value that is not a tenant id, or is an id reserved for "no tenant", finds no tenant.

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

### A header is a claim, not proof

A trusted caller can still name any tenant. Where a calling service should act only for some tenants, check that with
an [access validator](access-control.md). A service that only other services call should not be reachable from
outside at all. Where the calling service already sends a token that names the tenant, `ResolveFromClaim` reads it from
the token instead, and the header is not needed.

## Jobs and messages

Tenantry.Pro's Hangfire, MassTransit, Quartz.NET and Rebus integrations carry the tenant under the same name, as a
job parameter or message header, so a job that calls a service with `UseTenantry()` calls it as the job's tenant.

## See also

- [Tenant resolution](tenant-resolution.md) — the other resolvers, and their order
- [Access control](access-control.md) — access validators
- [Diagnostics](diagnostics.md) — the `tenant.id` tag and the `TenantId` log scope
