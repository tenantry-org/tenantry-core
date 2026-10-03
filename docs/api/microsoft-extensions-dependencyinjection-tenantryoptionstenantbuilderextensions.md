# `TenantryOptionsTenantBuilderExtensions` class

Namespace: `Microsoft.Extensions.DependencyInjection` · Package: `Tenantry.Options` · [API reference](README.md)

Options with values per tenant.

```csharp
public static class TenantryOptionsTenantBuilderExtensions
```

## Methods

### `ConfigurePerTenant<TOptions>(ITenantBuilder, Action<TOptions, ITenantDescriptor, IServiceProvider>)`

Configures `TOptions` per tenant, as [`TenantryOptionsTenantBuilderExtensions.ConfigurePerTenant<TOptions>`](microsoft-extensions-dependencyinjection-tenantryoptionstenantbuilderextensions.md) does, with the services of a scope created for the step: for settings read from a database. The scope is disposed after the step, and the tenant is current while it runs.

```csharp
public static ITenantBuilder ConfigurePerTenant<TOptions>(this ITenantBuilder builder, Action<TOptions, ITenantDescriptor, IServiceProvider> configure) where TOptions : class
```

Type parameters:

- `TOptions`: The options type.

Parameters:

- `builder` [`ITenantBuilder`](tenantry-itenantbuilder.md): The tenant builder.
- `configure` `Action<TOptions, ITenantDescriptor, IServiceProvider>`: Sets the tenant's values, with the tenant and the scope's services.

Returns: [`ITenantBuilder`](tenantry-itenantbuilder.md): The same `builder` for chaining.

Options have no asynchronous configuration, so the step runs synchronously, once per tenant until the tenant is invalidated.

```csharp
tenant.ConfigurePerTenant<BrandingOptions>((options, t, services) =>
    options.Colour = services.GetRequiredService<AppDbContext>().Settings.Single(s => s.Key == "colour").Value);
```

### `ConfigurePerTenant<TOptions>(ITenantBuilder, Action<TOptions, ITenantDescriptor>)`

Configures `TOptions` per tenant: `IOptions<TOptions>`, `IOptionsSnapshot<TOptions>` and `IOptionsMonitor<TOptions>` give the current tenant's value, built from the ordinary configuration (every `Configure`), then `configure` with the tenant. Without a tenant they give the ordinary value.

```csharp
public static ITenantBuilder ConfigurePerTenant<TOptions>(this ITenantBuilder builder, Action<TOptions, ITenantDescriptor> configure) where TOptions : class
```

Type parameters:

- `TOptions`: The options type.

Parameters:

- `builder` [`ITenantBuilder`](tenantry-itenantbuilder.md): The tenant builder.
- `configure` `Action<TOptions, ITenantDescriptor>`: Sets the tenant's values, with the tenant (read your tenant type with `As<T>()`).

Returns: [`ITenantBuilder`](tenantry-itenantbuilder.md): The same `builder` for chaining.

Each tenant's value is built on first use and cached; [`ITenantStoreCache<TKey>.Invalidate`](tenantry-itenantstorecache.md) clears it, so changing a tenant's settings is followed by invalidating the tenant. A change to the configuration the options are bound to clears every tenant's value. Validation (`Validate`, `IValidateOptions`) runs on each tenant's value when it is built, and `ValidateOnStart` validates the ordinary one.

It applies to the default-named options. It has a type parameter of its own, so it returns the non-generic builder: call it after the methods that need the tenant key type.

```csharp
builder.Services.Configure<BrandingOptions>(builder.Configuration.GetSection("Branding"));   // the defaults
builder.Services.AddTenantry<Guid>(tenant => tenant
    .UseStore<AppTenantStore>()
    .ConfigurePerTenant<BrandingOptions>((options, t) => options.Colour = t.As<AppTenant>().BrandColour));
```
