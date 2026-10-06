# `TenantryHttpClientBuilderExtensions` class

Namespace: `Microsoft.Extensions.DependencyInjection` · Package: `Tenantry.Http` · [API reference](README.md)

Sends the current tenant with an HTTP or gRPC client's requests.

```csharp
public static class TenantryHttpClientBuilderExtensions
```

## Methods

### `UseTenantry(IHttpClientBuilder, Uri?)`

Adds the current tenant's id to the client's requests, in the [`TenantPropagation.HeaderName`](tenantry-tenantpropagation.md) header, formatted by [`TenantIds.Format<TKey>`](tenantry-tenantids.md), for the service it calls to resolve with Tenantry.AspNetCore's `ResolveFromPropagationHeader(...)`.

```csharp
public static IHttpClientBuilder UseTenantry(this IHttpClientBuilder builder, Uri? serviceAddress = null)
```

Parameters:

- `builder` `IHttpClientBuilder`: The client's builder, from `AddHttpClient` or `AddGrpcClient`.
- `serviceAddress` `Uri`: The address of the service the client calls, for a client whose registration sets no `BaseAddress`: a gRPC client's `Address`, or the address a typed client sets in its constructor. Only its scheme, host and port are used.

Returns: `IHttpClientBuilder`: The same `builder` for chaining.

Exceptions:

- `InvalidOperationException`: `builder` is `ConfigureHttpClientDefaults`'s, which configures every client, third-party SDKs' included. The application has no `tenant.AddHttpPropagation()`, or the client has neither `serviceAddress` nor an absolute `BaseAddress` in its registration (thrown as the host starts, or, in a service provider built without a host, when the client is created). A request already carries the header with an id other than the current tenant's, or with no tenant current, or the tenant's id is not printable ASCII without a space at either end (thrown when the request is sent).
- `ArgumentException`: `serviceAddress` is not absolute.

Requires `tenant.AddHttpPropagation()` in `AddTenantry`. A request that already carries the header (forwarded from an incoming request, or from the client's `DefaultRequestHeaders`) throws `InvalidOperationException`, unless it names the current tenant. With no tenant current, the request goes without the header, and the receiving service decides what that means, for example with `RequireTenant()`.

The header goes only to the scheme, host and port of `serviceAddress`, or else of the base address set in the client's registration (`AddHttpClient(c => c.BaseAddress = …)`), not to an absolute address elsewhere. A redirect the client follows keeps it. The id must be printable ASCII with no space at either end.

```csharp
builder.Services.AddHttpClient<BillingClient>(c => c.BaseAddress = new Uri("https://billing.internal"))
    .UseTenantry();

var inventory = new Uri("https://inventory.internal");
builder.Services.AddGrpcClient<Inventory.InventoryClient>(o => o.Address = inventory)
    .UseTenantry(inventory);
```
