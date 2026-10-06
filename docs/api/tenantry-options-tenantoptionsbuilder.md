# `TenantOptionsBuilder<TKey>` class

Namespace: `Tenantry.Options` · Package: `Tenantry.Options` · [API reference](README.md)

Sets options per tenant, in `tenant.ConfigurePerTenant(...)`: each `Configure` or `ConfigureAll` names an options type and what differs for each tenant.

`IOptionsSnapshot<TOptions>` and `IOptionsMonitor<TOptions>` then give the current tenant's value, built from the ordinary configuration, then these steps with the tenant. Without a tenant they give the ordinary value. `IOptions<TOptions>` always gives the ordinary value.

`IOptions<TOptions>` is not per tenant because its value is read once and kept, often in a singleton's constructor. Read `IOptionsSnapshot<TOptions>`, which is scoped, in request code, and in a singleton hold `IOptionsMonitor<TOptions>` and read `CurrentValue` each time, not once in the constructor. The first read of `IOptions<TOptions>` while a tenant is current logs a warning, event 2008 in the category `Tenantry.Options`.

Each tenant's value is built on first use and cached until [`ITenantInvalidator<TKey>.InvalidateAsync`](tenantry-itenantinvalidator.md) clears it, so invalidate a tenant after changing its settings. With a store, the value is built from the store's copy of the tenant, read once per value built, not from the copy that is current; a value for an id the store does not hold is built from the current copy on every read and not kept. When the store answers with a tenant whose id differs from the one asked for, as a store that matches ids without regard to case can, the value is built from the store's copy and not kept either, since invalidating the store's id would not clear it. Without a store, the value is built from the current copy and kept. A change to the configuration the options are bound to clears every tenant's value. Validation (`Validate`, `IValidateOptions`) runs on each tenant's value when it is built, and `ValidateOnStart` validates the ordinary one. The tenant's steps run after every `Configure` and before every `PostConfigure`, in the order they are added.

```csharp
public sealed class TenantOptionsBuilder<TKey> where TKey : IEquatable<TKey>, IParsable<TKey>
```

## Type parameters

- `TKey`: The tenant identifier type.

## Methods

### `ConfigureAll<TOptions>(Action<TOptions, ITenantDescriptor<TKey>, IServiceProvider>)`

Sets `TOptions` per tenant for the default options and every named instance, with the services of a scope created for the step.

```csharp
public TenantOptionsBuilder<TKey> ConfigureAll<TOptions>(Action<TOptions, ITenantDescriptor<TKey>, IServiceProvider> configure) where TOptions : class
```

Type parameters:

- `TOptions`: The options type.

Parameters:

- `configure` `Action<TOptions, ITenantDescriptor<TKey>, IServiceProvider>`: Sets the tenant's values, with the tenant and the scope's services.

Returns: [`TenantOptionsBuilder<TKey>`](tenantry-options-tenantoptionsbuilder.md): This builder, for more options types.

### `ConfigureAll<TOptions>(Action<TOptions, ITenantDescriptor<TKey>>)`

Sets `TOptions` per tenant for the default options and every named instance: every authentication scheme of the type, for example.

```csharp
public TenantOptionsBuilder<TKey> ConfigureAll<TOptions>(Action<TOptions, ITenantDescriptor<TKey>> configure) where TOptions : class
```

Type parameters:

- `TOptions`: The options type.

Parameters:

- `configure` `Action<TOptions, ITenantDescriptor<TKey>>`: Sets the tenant's values, with the tenant.

Returns: [`TenantOptionsBuilder<TKey>`](tenantry-options-tenantoptionsbuilder.md): This builder, for more options types.

### `Configure<TOptions>(Action<TOptions, ITenantDescriptor<TKey>, IServiceProvider>)`

Sets the default-named `TOptions` per tenant with the services of a scope created for the step: for settings read from a database. The scope is disposed after the step, and the tenant is current while it runs.

```csharp
public TenantOptionsBuilder<TKey> Configure<TOptions>(Action<TOptions, ITenantDescriptor<TKey>, IServiceProvider> configure) where TOptions : class
```

Type parameters:

- `TOptions`: The options type.

Parameters:

- `configure` `Action<TOptions, ITenantDescriptor<TKey>, IServiceProvider>`: Sets the tenant's values, with the tenant and the scope's services.

Returns: [`TenantOptionsBuilder<TKey>`](tenantry-options-tenantoptionsbuilder.md): This builder, for more options types.

Options have no asynchronous configuration, so the step runs synchronously, once per tenant until the tenant is invalidated. A step that throws runs again on the next read.

```csharp
tenant.ConfigurePerTenant(perTenant => perTenant
    .Configure<BrandingOptions>((options, t, services) =>
        options.Colour = services.GetRequiredService<AppDbContext>().Settings.Single(s => s.Key == "colour").Value));
```

### `Configure<TOptions>(Action<TOptions, ITenantDescriptor<TKey>>)`

Sets the default-named `TOptions` per tenant.

```csharp
public TenantOptionsBuilder<TKey> Configure<TOptions>(Action<TOptions, ITenantDescriptor<TKey>> configure) where TOptions : class
```

Type parameters:

- `TOptions`: The options type.

Parameters:

- `configure` `Action<TOptions, ITenantDescriptor<TKey>>`: Sets the tenant's values, with the tenant (read your tenant type with `As<T>()`).

Returns: [`TenantOptionsBuilder<TKey>`](tenantry-options-tenantoptionsbuilder.md): This builder, for more options types.

```csharp
tenant.ConfigurePerTenant(perTenant => perTenant
    .Configure<BrandingOptions>((options, t) => options.Colour = t.As<AppTenant>().BrandColour));
```

### `Configure<TOptions>(string, Action<TOptions, ITenantDescriptor<TKey>, IServiceProvider>)`

Sets the `TOptions` named `name` per tenant with the services of a scope created for the step.

```csharp
public TenantOptionsBuilder<TKey> Configure<TOptions>(string name, Action<TOptions, ITenantDescriptor<TKey>, IServiceProvider> configure) where TOptions : class
```

Type parameters:

- `TOptions`: The options type.

Parameters:

- `name` `string`: The options' name, such as an authentication scheme's.
- `configure` `Action<TOptions, ITenantDescriptor<TKey>, IServiceProvider>`: Sets the tenant's values, with the tenant and the scope's services.

Returns: [`TenantOptionsBuilder<TKey>`](tenantry-options-tenantoptionsbuilder.md): This builder, for more options types.

### `Configure<TOptions>(string, Action<TOptions, ITenantDescriptor<TKey>>)`

Sets the `TOptions` named `name` per tenant: an authentication scheme's, for example, which its handler reads with `IOptionsMonitor<TOptions>.Get(scheme)`.

```csharp
public TenantOptionsBuilder<TKey> Configure<TOptions>(string name, Action<TOptions, ITenantDescriptor<TKey>> configure) where TOptions : class
```

Type parameters:

- `TOptions`: The options type.

Parameters:

- `name` `string`: The options' name, such as an authentication scheme's.
- `configure` `Action<TOptions, ITenantDescriptor<TKey>>`: Sets the tenant's values, with the tenant.

Returns: [`TenantOptionsBuilder<TKey>`](tenantry-options-tenantoptionsbuilder.md): This builder, for more options types.

The steps run before every `PostConfigure`, so an authentication handler's post-configuration (which builds JWT bearer's and OpenID Connect's metadata manager from `Authority`) sees the tenant's values. For authentication, the tenant must be current before the authentication middleware runs: see `app.UseTenantResolution()`.

```csharp
tenant.ConfigurePerTenant(perTenant => perTenant
    .Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, (options, t) =>
        options.Authority = t.As<AppTenant>().Authority));
```
