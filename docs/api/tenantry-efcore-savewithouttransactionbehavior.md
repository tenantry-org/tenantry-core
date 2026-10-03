# `SaveWithoutTransactionBehavior` enum

Namespace: `Tenantry.EfCore` · Package: `Tenantry.EfCore` · [API reference](README.md)

What `SaveChanges` does, with `Database.AutoTransactionBehavior` set to `Never` and no transaction, when the tenant check of some rows it writes is another of its statements: owned rows in a table of their own, checked by their owner's statement, and the rows of an entity mapped to more than one table (table-per-type, entity splitting), checked in the table with `TenantId`. Without a transaction, a statement EF Core sends beside a check that fails would stay written. Set with [`EfCoreIsolationOptions.OnSaveWithoutTransaction`](tenantry-efcore-efcoreisolationoptions.md).

```csharp
public enum SaveWithoutTransactionBehavior
```

## Values

| Value | Description |
|-------|-------------|
| `UseTransaction = 0` | Run that save in a transaction EF Core begins and commits itself, as it does by default (`AutoTransactionBehavior.WhenNeeded`), and set `Never` back when the save ends. Other saves stay without one. A transaction begun on the connection through ADO.NET must be handed to EF Core with `Database.UseTransaction`, or EF Core cannot begin its own and the save fails. The default. |
| `Reject = 1` | Throw [`TenantIsolationViolationException`](tenantry-efcore-tenantisolationviolationexception.md), of kind [`TenantIsolationViolationKind.SaveWithoutTransaction`](tenantry-efcore-tenantisolationviolationkind.md), before anything is sent. For a database or connection pooler that cannot run transactions. |
