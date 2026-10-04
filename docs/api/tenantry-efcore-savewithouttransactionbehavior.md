# `SaveWithoutTransactionBehavior` enum

Namespace: `Tenantry.EfCore` · Package: `Tenantry.EfCore` · [API reference](README.md)

What `SaveChanges` does when `Database.AutoTransactionBehavior` is `Never`, no transaction is open, and some rows it writes are tenant-checked by another of its statements. Set with [`EfCoreIsolationOptions.OnSaveWithoutTransaction`](tenantry-efcore-efcoreisolationoptions.md).

These rows are owned entities in a table of their own, checked by their owner's statement, and entities mapped to more than one table (table-per-type, entity splitting), checked in the table that has `TenantId`. Without a transaction, the other statements stay written if that check fails.

With [`SaveWithoutTransactionBehavior.UseTransaction`](tenantry-efcore-savewithouttransactionbehavior.md), other saves stay without a transaction. A transaction begun on the connection through ADO.NET must be handed to EF Core with `Database.UseTransaction`, or EF Core cannot begin its own and the save fails.

```csharp
public enum SaveWithoutTransactionBehavior
```

## Values

| Value | Description |
|-------|-------------|
| `UseTransaction = 0` | Run that save in a transaction EF Core begins and commits itself, as it does by default (`AutoTransactionBehavior.WhenNeeded`), and set `Never` back when the save ends. The default. |
| `Reject = 1` | Throw [`TenantIsolationViolationException`](tenantry-efcore-tenantisolationviolationexception.md), of kind [`TenantIsolationViolationKind.SaveWithoutTransaction`](tenantry-efcore-tenantisolationviolationkind.md), before anything is sent. For a database or connection pooler that cannot run transactions. |
