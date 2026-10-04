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
| `AccessDenied = 2` | The tenant is inactive (`ValidateTenantActivity`), or an access validator refused it. |
