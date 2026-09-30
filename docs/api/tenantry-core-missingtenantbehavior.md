# `MissingTenantBehavior` enum

Namespace: `Tenantry.Core` · Package: `Tenantry.Core` · [API reference](README.md)

Policy for what happens when a tenant-scoped operation runs without a resolved tenant context. Shared across EF Core write isolation and background-job tenant propagation so the behaviour is configured with a single vocabulary.

```csharp
public enum MissingTenantBehavior
```

## Values

| Value | Description |
|-------|-------------|
| `Allow = 0` | Proceed without a tenant, silently. EF Core saves updates and deletes without checking their tenant, and saves a new entity only if its `TenantId` is set; a background job runs without a tenant scope. |
| `Warn = 1` | As [`MissingTenantBehavior.Allow`](tenantry-core-missingtenantbehavior.md), but log a structured warning. Surfaces endpoints, jobs, or middleware ordering that bypassed tenant resolution without failing the operation. The default for job and message propagation in Tenantry.Pro. |
| `Reject = 2` | Fail the operation. EF Core writes throw [`TenantNotResolvedException`](tenantry-core-exceptions-tenantnotresolvedexception.md) before anything is persisted; a background job throws and is left to the host's retry/error handling. The default for EF Core write isolation. |
| `Skip = 3` | Abandon the operation without raising an error. Only for background-job propagation, where the handler does not run; what happens to the job or message then depends on the host (see each integration's guide). EF Core write isolation does not accept it: setting it there throws `ArgumentOutOfRangeException`. |
