# `TenantryOptionsTenantBuilderExtensions` class

Namespace: `Microsoft.Extensions.DependencyInjection` · Package: `Tenantry.Options` · [API reference](README.md)

Options with values per tenant.

```csharp
public static class TenantryOptionsTenantBuilderExtensions
```

## Methods

### `ConfigureAllPerTenant<TOptions>(ITenantBuilder, Action<TOptions, ITenantDescriptor, IServiceProvider>)`

Configures `TOptions` per tenant, as [`TenantryOptionsTenantBuilderExtensions.ConfigureAllPerTenant<TOptions>`](microsoft-extensions-dependencyinjection-tenantryoptionstenantbuilderextensions.md) does, with the services of a scope created for the step.

```csharp
public static ITenantBuilder ConfigureAllPerTenant<TOptions>(this ITenantBuilder builder, Action<TOptions, ITenantDescriptor, IServiceProvider> configure) where TOptions : class
```

Type parameters:

- `TOptions`: The options type.

Parameters:

- `builder` [`ITenantBuilder`](tenantry-itenantbuilder.md): The tenant builder.
- `configure` `Action<TOptions, ITenantDescriptor, IServiceProvider>`: Sets the tenant's values, with the tenant and the scope's services.

Returns: [`ITenantBuilder`](tenantry-itenantbuilder.md): The same `builder` for chaining.

### `ConfigureAllPerTenant<TOptions>(ITenantBuilder, Action<TOptions, ITenantDescriptor>)`

Configures `TOptions` per tenant, as [`TenantryOptionsTenantBuilderExtensions.ConfigurePerTenant<TOptions>`](microsoft-extensions-dependencyinjection-tenantryoptionstenantbuilderextensions.md) does, for the default options and every named instance: every authentication scheme of the type, for example.

```csharp
public static ITenantBuilder ConfigureAllPerTenant<TOptions>(this ITenantBuilder builder, Action<TOptions, ITenantDescriptor> configure) where TOptions : class
```

Type parameters:

- `TOptions`: The options type.

Parameters:

- `builder` [`ITenantBuilder`](tenantry-itenantbuilder.md): The tenant builder.
- `configure` `Action<TOptions, ITenantDescriptor>`: Sets the tenant's values, with the tenant.

Returns: [`ITenantBuilder`](tenantry-itenantbuilder.md): The same `builder` for chaining.

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

Configures `TOptions` per tenant: `IOptionsSnapshot<TOptions>` and `IOptionsMonitor<TOptions>` give the current tenant's value, built from the ordinary configuration (every `Configure`), then `configure` with the tenant. Without a tenant they give the ordinary value. `IOptions<TOptions>` always gives the ordinary value.

```csharp
public static ITenantBuilder ConfigurePerTenant<TOptions>(this ITenantBuilder builder, Action<TOptions, ITenantDescriptor> configure) where TOptions : class
```

Type parameters:

- `TOptions`: The options type.

Parameters:

- `builder` [`ITenantBuilder`](tenantry-itenantbuilder.md): The tenant builder.
- `configure` `Action<TOptions, ITenantDescriptor>`: Sets the tenant's values, with the tenant (read your tenant type with `As<T>()`).

Returns: [`ITenantBuilder`](tenantry-itenantbuilder.md): The same `builder` for chaining.

`IOptions<TOptions>` is not per tenant because its value is read once and kept: a singleton that reads `options.Value` in its constructor would keep the first tenant's settings and use them for every tenant. Read `IOptionsSnapshot<TOptions>`, which is scoped, in request code, and hold `IOptionsMonitor<TOptions>` in a singleton and read `CurrentValue` each time. Reading `CurrentValue` once in a constructor keeps one tenant's value, as reading `Value` would.

Each tenant's value is built on first use and cached; [`ITenantInvalidator<TKey>.InvalidateAsync`](tenantry-itenantinvalidator.md) clears it, so changing a tenant's settings is followed by invalidating the tenant. A change to the configuration the options are bound to clears every tenant's value. Validation (`Validate`, `IValidateOptions`) runs on each tenant's value when it is built, and `ValidateOnStart` validates the ordinary one.

It applies to the default-named options; for named options, such as an authentication scheme's, use the overload that takes a name, or `ConfigureAllPerTenant`. The tenant's steps run after every `Configure` and before every `PostConfigure`. It has a type parameter of its own, so it returns the non-generic builder: call it after the methods that need the tenant key type.

```csharp
builder.Services.Configure<BrandingOptions>(builder.Configuration.GetSection("Branding"));   // the defaults
builder.Services.AddTenantry<Guid>(tenant => tenant
    .UseStore<AppTenantStore>()
    .ConfigurePerTenant<BrandingOptions>((options, t) => options.Colour = t.As<AppTenant>().BrandColour));
```

### `ConfigurePerTenant<TOptions>(ITenantBuilder, string, Action<TOptions, ITenantDescriptor, IServiceProvider>)`

Configures `TOptions` per tenant, as [`TenantryOptionsTenantBuilderExtensions.ConfigurePerTenant<TOptions>`](microsoft-extensions-dependencyinjection-tenantryoptionstenantbuilderextensions.md) does, with the services of a scope created for the step.

```csharp
public static ITenantBuilder ConfigurePerTenant<TOptions>(this ITenantBuilder builder, string name, Action<TOptions, ITenantDescriptor, IServiceProvider> configure) where TOptions : class
```

Type parameters:

- `TOptions`: The options type.

Parameters:

- `builder` [`ITenantBuilder`](tenantry-itenantbuilder.md): The tenant builder.
- `name` `string`: The options' name, such as an authentication scheme's.
- `configure` `Action<TOptions, ITenantDescriptor, IServiceProvider>`: Sets the tenant's values, with the tenant and the scope's services.

Returns: [`ITenantBuilder`](tenantry-itenantbuilder.md): The same `builder` for chaining.

### `ConfigurePerTenant<TOptions>(ITenantBuilder, string, Action<TOptions, ITenantDescriptor>)`

Configures `TOptions` per tenant, as [`TenantryOptionsTenantBuilderExtensions.ConfigurePerTenant<TOptions>`](microsoft-extensions-dependencyinjection-tenantryoptionstenantbuilderextensions.md) does, for the options named `name`: an authentication scheme's, for example, which its handler reads with `IOptionsMonitor<TOptions>.Get(scheme)`.

```csharp
public static ITenantBuilder ConfigurePerTenant<TOptions>(this ITenantBuilder builder, string name, Action<TOptions, ITenantDescriptor> configure) where TOptions : class
```

Type parameters:

- `TOptions`: The options type.

Parameters:

- `builder` [`ITenantBuilder`](tenantry-itenantbuilder.md): The tenant builder.
- `name` `string`: The options' name, such as an authentication scheme's.
- `configure` `Action<TOptions, ITenantDescriptor>`: Sets the tenant's values, with the tenant.

Returns: [`ITenantBuilder`](tenantry-itenantbuilder.md): The same `builder` for chaining.

The tenant's steps run after every `Configure` of the name and before every `PostConfigure`, so an authentication handler's post-configuration (which builds JWT bearer's and OpenID Connect's metadata manager from `Authority`) sees the tenant's values. For authentication, the tenant must be current before the authentication middleware runs: see `app.UseTenantResolution()`.

```csharp
tenant.ConfigurePerTenant<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, (options, t) =>
    options.Authority = t.As<AppTenant>().Authority);
```
