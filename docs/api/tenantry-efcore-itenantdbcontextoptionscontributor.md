# `ITenantDbContextOptionsContributor` interface

Namespace: `Tenantry.EfCore` · Package: `Tenantry.EfCore` · [API reference](README.md)

An extension point: for code that extends the package, such as another package that builds on it. An application rarely needs it.

Adds to the options of every `DbContext` that uses `UseTenantry()`.

Register implementations in the application's service collection, as singletons. Packages that build on Tenantry use this to configure contexts without asking the application to add another call to each one, for example to add an interceptor. Contributors run when `UseTenantry()` is called with the application service provider in place, as it is inside `AddDbContext`, `AddDbContextPool`, `AddDbContextFactory`, `AddPooledDbContextFactory` and `AddDbContextPerTenantDatabase`; options built by hand without it run none. Several of these build a context's options only once (pooling, `AddDbContextFactory`, `AddDbContextPerTenantDatabase`), so a contributor must never depend on the current tenant.

```csharp
[EditorBrowsable(EditorBrowsableState.Advanced)]
public interface ITenantDbContextOptionsContributor
```

## Methods

### `Configure(DbContextOptionsBuilder)`

Configures a context's options. `optionsBuilder.Options.ContextType` is the context being configured.

```csharp
void Configure(DbContextOptionsBuilder optionsBuilder)
```

Parameters:

- `optionsBuilder` `DbContextOptionsBuilder`: The options builder `UseTenantry()` was called on.
