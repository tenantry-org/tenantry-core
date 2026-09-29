# `MissingTenantBehavior` enum

Namespace: `Tenantry.Core` · Package: `Tenantry.Core` · [API reference](README.md)

Policy for what happens when a tenant-scoped operation runs without a resolved tenant context. Shared across EF Core write isolation and background-job tenant propagation so the behaviour is configured with a single vocabulary.

```csharp
public enum MissingTenantBehavior
```

## Values

| Value | Description |
|-------|-------------|
| `Allow = 0` | Proceed without a tenant, silently. EF Core writes are saved without a stamped `TenantId`; a background job runs without a tenant scope. |
| `Warn = 1` | Proceed without a tenant, but log a structured warning. The default — surfaces endpoints, jobs, or middleware ordering that bypassed tenant resolution without failing the operation. |
| `Reject = 2` | Fail the operation. EF Core writes throw [`TenantNotResolvedException`](tenantry-core-exceptions-tenantnotresolvedexception.md) before anything is persisted; a background job throws and is left to the host's retry/error handling. |
| `Skip = 3` | Abandon the operation without raising an error. Intended for background-job propagation, where it means "acknowledge and drop the message/job without running the handler". For EF Core write isolation there is nothing to abandon, so this behaves like [`MissingTenantBehavior.Allow`](tenantry-core-missingtenantbehavior.md) (the save proceeds without a stamped tenant). |
