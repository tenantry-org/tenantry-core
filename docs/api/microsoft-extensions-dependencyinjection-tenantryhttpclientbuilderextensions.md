# `TenantryHttpClientBuilderExtensions` class

Namespace: `Microsoft.Extensions.DependencyInjection` · Package: `Tenantry.Http` · [API reference](README.md)

Sends the current tenant with an HTTP or gRPC client's requests.

```csharp
public static class TenantryHttpClientBuilderExtensions
```

## Methods

### `UseTenantry(IHttpClientBuilder)`

Adds the current tenant's id to the client's requests, in the [`TenantPropagation.HeaderName`](tenantry-tenantpropagation.md) header, formatted by [`TenantIds.Format<TKey>`](tenantry-tenantids.md), for the service it calls to resolve with Tenantry.AspNetCore's `ResolveFromPropagationHeader(...)`. Requires `tenant.AddHttpPropagation()` in `AddTenantry`.

```csharp
public static IHttpClientBuilder UseTenantry(this IHttpClientBuilder builder)
```

Parameters:

- `builder` `IHttpClientBuilder`: The client's builder, from `AddHttpClient` or `AddGrpcClient`.

Returns: `IHttpClientBuilder`: The same `builder` for chaining.

Exceptions:

- `InvalidOperationException`: `builder` is `ConfigureHttpClientDefaults`'s, which configures every client, third-party SDKs' included. The client is created without `tenant.AddHttpPropagation()` (thrown when it is created). A request sent while a tenant is current already carries the header with another tenant's id, or the tenant's id is not printable ASCII without a space at either end (thrown when the request is sent).

While a tenant is current, a request carries the tenant's id in the header. A request that already carries the header with another tenant's id (forwarded from an incoming request, or from the client's `DefaultRequestHeaders`) throws `InvalidOperationException`. With no tenant current, the request goes as the caller built it, and the receiving service decides what a missing header means, for example with `RequireTenant()`.

The header goes only to the service the client is for: when the client has a base address, set in its configuration (`AddHttpClient(c => c.BaseAddress = …)`), only requests to that scheme, host and port carry it; a request to an absolute address elsewhere does not. A client with no base address there (a gRPC client, or a typed client that sets it in its constructor) carries it on every request. A redirect that the client follows keeps the request's headers, this one included.

The id must be printable ASCII with no space at either end, as a header carries it; a request as a tenant whose id is not throws `InvalidOperationException`.

```csharp
builder.Services.AddHttpClient<BillingClient>(c => c.BaseAddress = new Uri("https://billing.internal"))
    .UseTenantry();
```
