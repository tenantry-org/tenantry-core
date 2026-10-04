# `TenantryOptionsTenantBuilderExtensions` class

Namespace: `Microsoft.Extensions.DependencyInjection` · Package: `Tenantry.Options` · [API reference](README.md)

Options with values per tenant.

```csharp
public static class TenantryOptionsTenantBuilderExtensions
```

## Methods

### `ConfigurePerTenant<TKey>(ITenantBuilder<TKey>, Action<TenantOptionsBuilder<TKey>>)`

Configures options per tenant: `IOptionsSnapshot<TOptions>` and `IOptionsMonitor<TOptions>` give the current tenant's value of each options type `configure` names.

```csharp
public static ITenantBuilder<TKey> ConfigurePerTenant<TKey>(this ITenantBuilder<TKey> builder, Action<TenantOptionsBuilder<TKey>> configure) where TKey : IEquatable<TKey>, IParsable<TKey>
```

Type parameters:

- `TKey`: The tenant identifier type.

Parameters:

- `builder` [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The tenant builder.
- `configure` `Action<TenantOptionsBuilder<TKey>>`: Names the options types and sets their values per tenant.

Returns: [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The same `builder` for chaining.

`IOptions<TOptions>` always gives the ordinary value. See [`TenantOptionsBuilder<TKey>`](tenantry-options-tenantoptionsbuilder.md).

```csharp
builder.Services.Configure<BrandingOptions>(builder.Configuration.GetSection("Branding"));   // the defaults
builder.Services.AddTenantry<Guid>(tenant => tenant
    .UseStore<AppTenantStore>()
    .ConfigurePerTenant(perTenant => perTenant
        .Configure<BrandingOptions>((options, t) => options.Colour = t.As<AppTenant>().BrandColour)
        .Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, (options, t) =>
            options.Authority = t.As<AppTenant>().Authority)));
```
