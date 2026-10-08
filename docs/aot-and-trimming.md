# AOT & trimming

Every Tenantry package is trimmable, and every one but `Tenantry.EfCore` supports Native AOT. Tenantry builds with the
trim and AOT analyzers on.

## Summary

| Package               | `IsTrimmable` | `IsAotCompatible` | Native AOT | Notes |
|-----------------------|:-------------:|:-----------------:|:----------:|-------|
| `Tenantry.Core`       | ✅ | ✅ | ✅ | No reflection beyond annotated, AOT-safe DI patterns. |
| `Tenantry.AspNetCore` | ✅ | ✅ | ✅ | Demonstrated by the `Aot` sample. |
| `Tenantry.Http`       | ✅ | ✅ | ✅ | Demonstrated by the `Aot` sample, and checked by a Native AOT build in CI. |
| `Tenantry.Caching`    | ✅ | ✅ | ✅ | Checked by a Native AOT build in CI. Microsoft's `HybridCache` implementation has trim warnings of its own (its default JSON serializer). |
| `Tenantry.Options`    | ✅ | ✅ | ✅ | Checked by a Native AOT build in CI. Binding options to configuration (`Configure<T>(section)`) has its own trimming rules: use the configuration binding source generator. |
| `Tenantry.EfCore`     | ✅ | No | ⚠️ Not supported | Its EF Core entry points are annotated, as EF Core's are. |

"Trimmable" means the package raises no trim warnings of its own. "AOT-compatible" means the same for Native AOT.

## `Tenantry.Core`, `Tenantry.AspNetCore`, `Tenantry.Http`, `Tenantry.Caching` and `Tenantry.Options`

For an AOT web app, use `Tenantry.Core` and `Tenantry.AspNetCore`, with a store over a client that supports AOT, such
as a hand-written `ITenantStore`.

Each of these packages is marked `IsAotCompatible` and `IsTrimmable`, and builds with no warnings under both
analyzers. Where the API takes a type that DI must construct, it is annotated so the trimmer keeps its constructors:

```csharp no-compile
ITenantBuilder<TKey> UseStore<[DynamicallyAccessedMembers(PublicConstructors)] TStore>()
    where TStore : class, ITenantStore<TKey>;
```

The [`Tenantry.Samples.Aot`](../samples/Tenantry.Samples.Aot) project is an ASP.NET Core app published with Native
AOT:

```xml
<PublishAot>true</PublishAot>
<InvariantGlobalization>true</InvariantGlobalization>
```

It uses `WebApplication.CreateSlimBuilder`, header-based resolution, the in-memory store, and source-generated
JSON (`JsonSerializerContext`). ASP.NET Core needs a `JsonSerializerContext` for the types your own AOT app
serialises too.

```bash
dotnet publish samples/Tenantry.Samples.Aot -c Release
```

## `Tenantry.EfCore`: trimmable, not AOT-compatible

Publish an EF Core app without Native AOT. Trimming works, but test the trimmed build: EF Core providers and your
model may need trim roots, as EF Core's guidance describes.

`Tenantry.EfCore` is marked `IsTrimmable` but not `IsAotCompatible`. `UseTenantry()` builds its query filters as
expression trees while EF Core builds the model at run time. Neither `Tenantry.EfCore` nor EF Core supports Native AOT
with a model built at run time.

Every public method that configures or creates an EF Core context or model carries `[RequiresUnreferencedCode]` and
`[RequiresDynamicCode]`, as EF Core's `DbContext` does: `UseTenantry()`, `AddDbContextPerTenantDatabase` and
`IsSharedAcrossTenants()`. The analyzers warn where your code calls them. Types that only read a model or check a
context, such as `TenantModel` and `TenantContextGuard`, are not annotated.
