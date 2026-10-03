# Options per tenant

Settings that differ between tenants (a limit that follows the tenant's plan, a brand colour, an upstream endpoint) can
stay in the options pattern your code already reads. `Tenantry.Options` makes `IOptions<T>`, `IOptionsSnapshot<T>` and
`IOptionsMonitor<T>` give the current tenant's value for the options types you name.

```bash
dotnet add package Tenantry.Options
```

## Configuring options per tenant

Configure the defaults as usual, then `ConfigurePerTenant<TOptions>` in `AddTenantry` with what differs per tenant:

```csharp
using Tenantry;

builder.Services.Configure<LimitsOptions>(builder.Configuration.GetSection("Limits"));   // the defaults

builder.Services.AddTenantry<Guid>(tenant => tenant
    .UseStore<EfCoreTenantStore>()
    .ConfigurePerTenant<LimitsOptions>((options, t) =>
    {
        if (t.As<AppTenant>().Plan == "enterprise")
            options.MaxUsers = 500;
    }));

public sealed class LimitsOptions
{
    public int MaxUsers { get; set; } = 20;
}
```

Code that reads the options is unchanged:

```csharp
using Microsoft.Extensions.Options;

app.MapGet("/limits", (IOptions<LimitsOptions> limits) => limits.Value.MaxUsers).RequireTenant();
```

- **Built from the ordinary configuration, then the tenant.** Each tenant's value starts from every `Configure`, in
  any order they were added, then the tenant's steps run, in the order added.
- **No tenant, no change.** Without a current tenant, the readers give the ordinary value.
- **Read where it is used.** `IOptions<T>.Value` reads the current tenant each time, so a singleton that holds
  `IOptions<T>` sees the tenant of the code that calls it, not the one it was created under.
- **Only the types you name.** Every other options type behaves as before.
- **The default name.** The steps apply to the default-named options; named options (`Get("name")`) are kept per
  tenant but get no tenant steps.
- `ConfigurePerTenant` has a type parameter of its own, so it returns the non-generic builder: call it after the
  methods that need the tenant key type, such as `UseStore`.

### Settings from a database

An overload gives the step the services of a scope created for it, with the tenant current, for settings read from a
database:

```csharp
builder.Services.AddTenantry<Guid>(tenant => tenant
    .UseStore<EfCoreTenantStore>()
    .ConfigurePerTenant<LimitsOptions>((options, t, services) =>
    {
        var catalog = services.GetRequiredService<CatalogDbContext>();
        options.MaxUsers = catalog.Tenants.Where(x => x.Name == t.Name).Select(x => x.Plan).Single() == "enterprise" ? 500 : 20;
    }));
```

The options pattern has no asynchronous configuration, so the step runs synchronously; it runs once per tenant, until
the tenant is invalidated (below).

## When settings change

Each tenant's value is built on first use and cached. To rebuild it after a tenant's settings change, invalidate the
tenant: `ITenantStoreCache<TKey>.Invalidate(tenantId)` clears its options with everything else Tenantry keeps for it
(see [Tenant stores](tenant-stores.md#everything-kept-for-a-tenant)), and `InvalidateAll()` clears every tenant's. A
change to the configuration the options are bound to (a reloaded `appsettings.json`) clears every tenant's value of
that options type. Each instance of the application has its own values, so invalidate on each one, or keep the
settings in configuration that reloads.

## Validation

`Validate` and `IValidateOptions<T>` run on each tenant's value when it is built, so reading the options as a tenant
whose settings are invalid throws `OptionsValidationException`. `ValidateOnStart` runs at startup, without a tenant, so
it validates the ordinary value.

## See also

- [Tenant stores](tenant-stores.md) — your tenant type, which the steps read with `As<T>()`
- [AOT & trimming](aot-and-trimming.md)
