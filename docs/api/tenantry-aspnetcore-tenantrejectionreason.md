# `TenantRejectionReason` enum

Namespace: `Tenantry.AspNetCore` · Package: `Tenantry.AspNetCore` · [API reference](README.md)

Why `app.UseTenantry()` rejects a request to an endpoint that needs a tenant.

```csharp
public enum TenantRejectionReason
```

## Values

| Value | Description |
|-------|-------------|
| `Missing = 0` | No resolver found an identifier in the request. |
| `NotFound = 1` | The identifier names no tenant. |
| `AccessDenied = 2` | An access validator refused the tenant. |
| `Inactive = 3` | The tenant is not active: a `ValidateTenantActivity` check refused it. Only a request the access validators allow is rejected for this reason. |
