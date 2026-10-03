# Options per tenant

Settings that differ between tenants (a limit that follows the tenant's plan, a brand colour, an upstream endpoint) can
stay in the options pattern. `Tenantry.Options` makes `IOptionsSnapshot<T>` and `IOptionsMonitor<T>` give the current
tenant's value for the options types you name. `IOptions<T>` keeps the ordinary value: see
[Why IOptions keeps the ordinary value](#why-ioptions-keeps-the-ordinary-value).

```bash
dotnet add package Tenantry.Options
```

## Configuring options per tenant

Configure the defaults as usual, then, in `AddTenantry`, name each options type in `ConfigurePerTenant` with what
differs per tenant:

```csharp
using Tenantry;

builder.Services.Configure<LimitsOptions>(builder.Configuration.GetSection("Limits"));   // the defaults

builder.Services.AddTenantry<Guid>(tenant => tenant
    .UseStore<EfCoreTenantStore>()
    .ConfigurePerTenant(perTenant => perTenant.Configure<LimitsOptions>((options, t) =>
    {
        if (t.As<AppTenant>().Plan == "enterprise")
            options.MaxUsers = 500;
    })));

public sealed class LimitsOptions
{
    public int MaxUsers { get; set; } = 20;
}
```

Read the tenant's value with `IOptionsSnapshot<T>` in request code, or with `IOptionsMonitor<T>`:

```csharp
using Microsoft.Extensions.Options;

app.MapGet("/limits", (IOptionsSnapshot<LimitsOptions> limits) => limits.Value.MaxUsers).RequireTenant();
```

- **Built from the ordinary configuration, then the tenant.** Each tenant's value starts from the ordinary
  configuration, then the tenant's steps run, in the order added.
- **No tenant, no change.** Without a current tenant, the readers give the ordinary value.
- **A singleton holds the monitor.** `IOptionsSnapshot<T>` is scoped, so scope validation refuses a singleton that
  depends on it. A singleton holds `IOptionsMonitor<T>` and reads `CurrentValue` each time it needs the value.
- **Only the types you name.** Every other options type behaves as before.
- **After `services.Configure`, before `PostConfigure`.** The tenant's steps run after every `services.Configure`, in
  any order they were added, and before every `PostConfigure`.
- **The default name, or a name you give.** `Configure<T>(configure)` applies to the default-named options,
  `Configure<T>(name, configure)` to one name (`Get("name")`), and `ConfigureAll<T>(configure)` to every name. For
  authentication schemes, see [Authentication per tenant](authentication-per-tenant.md).
- **Several types in one call.** `Configure` returns the builder, so one `ConfigurePerTenant` can set several options
  types, and `ConfigurePerTenant` returns the tenant builder, so the chain goes on after it.

### Why IOptions keeps the ordinary value

Code reads `IOptions<T>.Value` once and keeps it, often in a singleton's constructor:

```csharp
public sealed class LimitsChecker(IOptions<LimitsOptions> options)
{
    private readonly LimitsOptions _limits = options.Value;   // read once, kept for every caller

    public bool Allows(int users) => users <= _limits.MaxUsers;
}
```

If `IOptions<T>` gave the tenant's value, this singleton would keep the settings of the first tenant that used it and
apply them to every other tenant. Library code does the same, so the mistake could be in code you don't own. So
`IOptions<T>` always gives the ordinary value, built with no tenant current, and a tenant's settings never reach code
that keeps a value. Code that should see the tenant's settings reads `IOptionsSnapshot<T>` or `IOptionsMonitor<T>`.

The same applies to the monitor: reading `CurrentValue` once in a constructor keeps one tenant's value. Read it where
the value is used.

### Settings from a database

An overload gives the step the services of a scope created for it, with the tenant current, for settings read from a
database:

```csharp
builder.Services.AddTenantry<Guid>(tenant => tenant
    .UseStore<EfCoreTenantStore>()
    .ConfigurePerTenant(perTenant => perTenant.Configure<LimitsOptions>((options, t, services) =>
    {
        var catalog = services.GetRequiredService<CatalogDbContext>();
        options.MaxUsers = catalog.Tenants.Where(x => x.Name == t.Name).Select(x => x.Plan).Single() == "enterprise" ? 500 : 20;
    })));
```

The options pattern has no asynchronous configuration, so the step runs synchronously; it runs once per tenant, until
the tenant is invalidated (below). A step that throws is run again on the next read.

## When settings change

Each tenant's value is built on first use and cached. To rebuild it after a tenant's settings change, invalidate the
tenant: `ITenantInvalidator<TKey>.InvalidateAsync(tenantId)` clears its options with everything else Tenantry keeps for
it (see [Tenant stores](tenant-stores.md#everything-kept-for-a-tenant)), and `InvalidateAllAsync()` clears every
tenant's.
After an invalidation, a value is built from the tenant as the store has it then, even in a request that was resolved
before the invalidation and still carries the old copy. A change to the configuration the options are bound to (a
reloaded `appsettings.json`) clears every tenant's value of that options type. Each instance of the application has its
own values, so invalidate on each one, or keep the settings in configuration that reloads.

## Validation

`Validate` and `IValidateOptions<T>` run on each tenant's value when it is built, so reading the options as a tenant
whose settings are invalid throws `OptionsValidationException`. `ValidateOnStart` runs at startup, without a tenant, so
it validates the ordinary value.

## See also

- [Tenant stores](tenant-stores.md) — your tenant type, which the steps read with `As<T>()`
- [AOT & trimming](aot-and-trimming.md)
