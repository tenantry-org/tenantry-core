# `ITenantAccessValidator<TKey>` interface

Namespace: `Tenantry.AspNetCore` · Package: `Tenantry.AspNetCore` · [API reference](README.md)

Decides whether a request may use the tenant it names.

Register one with `tenant.ValidateTenantAccess<TValidator>()`. It is created in each request's scope, so it can depend on scoped services such as a `DbContext`. Every validator must allow a request before its tenant is made current. A request refused by one gets [`TenantResolutionOptions<TKey>.AccessDeniedStatusCode`](tenantry-aspnetcore-tenantresolutionoptions.md) on an endpoint that needs a tenant, and continues without a tenant on any other. With `app.UseTenantResolution()`, a signed-in request it refuses gets that status on every endpoint.

```csharp
public sealed class MembershipValidator(AppDbContext db) : ITenantAccessValidator<Guid>
{
    public async ValueTask<bool> ValidateAsync(HttpContext context, ITenantDescriptor<Guid> tenant, CancellationToken ct) =>
        await db.Memberships.AnyAsync(m => m.UserId == context.User.FindFirstValue("sub") && m.TenantId == tenant.TenantId, ct);
}
```

```csharp
public interface ITenantAccessValidator<TKey> where TKey : IEquatable<TKey>, IParsable<TKey>
```

## Type parameters

- `TKey`: The tenant identifier type.

## Methods

### `ValidateAsync(HttpContext, ITenantDescriptor<TKey>, CancellationToken)`

Returns [true](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/bool) when the request may use `tenant`.

```csharp
ValueTask<bool> ValidateAsync(HttpContext context, ITenantDescriptor<TKey> tenant, CancellationToken cancellationToken)
```

Parameters:

- `context` `HttpContext`: The request.
- `tenant` [`ITenantDescriptor<TKey>`](tenantry-itenantdescriptor-1.md): The tenant the request names, found in the tenant store.
- `cancellationToken` `CancellationToken`: The request's cancellation token.

Returns: `ValueTask<bool>`
