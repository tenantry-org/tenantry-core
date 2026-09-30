# `ITenantModelContributor` interface

Namespace: `Tenantry.EfCore` · Package: `Tenantry.EfCore` · [API reference](README.md)

Adds to the model of every `DbContext` that uses `UseTenantry()`. Register implementations in the application's service collection, as singletons.

Contributors run while EF Core builds the model, after the context's `OnModelCreating` and before Tenantry adds its tenant query filters, so an entity type a contributor adds is isolated too. EF Core builds a model once and caches it, by default once per context type. They are resolved from the context's application service provider: a context built without it (a design-time factory that builds its options by hand, say) gets a model without their contributions.

```csharp
public interface ITenantModelContributor
```

## Methods

### `Configure(ModelBuilder, DbContext)`

Configures a context's model.

```csharp
void Configure(ModelBuilder modelBuilder, DbContext context)
```

Parameters:

- `modelBuilder` `ModelBuilder`: The model builder, after `OnModelCreating`.
- `context` `DbContext`: The context the model is being built for.
