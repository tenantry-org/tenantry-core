# AOT & trimming

Tenantry is built with the .NET trim and AOT analyzers enabled (`EnableTrimAnalyzer`,
`EnableAotAnalyzer`) and is annotated honestly. This page states exactly what is supported, per
package, and why EF Core is different.

## Summary

| Package               | `IsTrimmable` | `IsAotCompatible` | Native AOT | Notes |
|-----------------------|:-------------:|:-----------------:|:----------:|-------|
| `Tenantry.Core`       | ✅ | ✅ | ✅ | No reflection beyond annotated, AOT-safe DI patterns. |
| `Tenantry.AspNetCore` | ✅ | ✅ | ✅ | Demonstrated by the `Aot` sample. |
| `Tenantry.EfCore`     | ✅ | — | ⚠️ Not supported | Query filters require dynamic code; matches EF Core's own AOT stance. |

"Trimmable" means the package is safe to include in a trimmed app and produces no trim warnings of its
own. "AOT-compatible" means the same for Native AOT (which also implies no run-time code generation).

## `Tenantry.Core` and `Tenantry.AspNetCore` — fully AOT & trim safe

Both are marked `IsAotCompatible` and `IsTrimmable` and compile clean under both analyzers. Where the
public API accepts a type that DI must construct, it is annotated so the trimmer preserves the needed
members — for example:

```csharp no-compile
ITenantBuilder<TKey> UseStore<[DynamicallyAccessedMembers(PublicConstructors)] TStore>()
    where TStore : class, ITenantStore<TKey>;
```

The [`Tenantry.Samples.Aot`](../samples/Tenantry.Samples.Aot) project is a complete ASP.NET Core app
published with Native AOT:

```xml
<PublishAot>true</PublishAot>
<InvariantGlobalization>true</InvariantGlobalization>
```

It uses `WebApplication.CreateSlimBuilder`, header-based resolution, the in-memory store, and
**source-generated JSON** (`JsonSerializerContext`) — the standard requirements for an AOT web app.
Your own AOT app must likewise supply a `JsonSerializerContext` for the types it serialises; that is an
ASP.NET Core/`System.Text.Json` requirement, not a Tenantry one.

```bash
dotnet publish samples/Tenantry.Samples.Aot -c Release
```

## `Tenantry.EfCore` — trim-compatible, not AOT-compatible

`Tenantry.EfCore` is marked `IsTrimmable` but **not** `IsAotCompatible`. `UseTenantry()` builds its tenant query
filters as LINQ expression trees while EF Core builds the model at run time, for entity types it only knows then.
Expression-tree construction for runtime types is what Native AOT cannot do.

This is consistent with **EF Core itself**, which does not support Native AOT with a model built at run time: EF
Core's `DbContext` constructors are annotated `[RequiresDynamicCode]` and `[RequiresUnreferencedCode]`, so your
own context already carries those warnings. So the practical rule is: *if you use the EF Core integration, you are
not in an AOT scenario.* Tenantry adds no annotations of its own on top of EF Core's.

## Recommendations

- **AOT web app:** use `Tenantry.Core` + `Tenantry.AspNetCore`. For data, use a store and persistence
  approach that is itself AOT-friendly (e.g. a hand-written `ITenantStore` over an AOT-safe client).
  The tenant context, resolution, middleware, and access control are all AOT-safe.
- **EF Core app:** use the full stack and publish without Native AOT. Trimming is supported; test your
  trimmed build, as EF Core providers and your model may still need trim roots configured per EF Core's
  guidance.
