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

```csharp
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

`Tenantry.EfCore` is marked `IsTrimmable` but **not** `IsAotCompatible`, and the read-side query-filter
APIs are explicitly annotated:

```csharp
[RequiresDynamicCode("Expression tree construction requires dynamic code generation.")]
[RequiresUnreferencedCode("Iterates model entity types and accesses members by name.")]
public static void ApplyTenantFilters<TKey, TContext>(this ModelBuilder modelBuilder, TContext context)
```

`MultiTenantDbContext<TKey>` carries the same annotations. The reason:

- `ApplyTenantFilters` reflects over the EF Core model and **builds LINQ expression trees** at runtime
  to compose the per-entity filter. Expression-tree compilation is exactly what Native AOT cannot do,
  hence `[RequiresDynamicCode]`.
- It also accesses entity members by name, which the trimmer cannot statically prove are kept, hence
  `[RequiresUnreferencedCode]`.

This is consistent with **EF Core itself**, which does not support Native AOT. So the practical rule
is: *if you use the EF Core integration, you are not in an AOT scenario.*

The write-side interceptor does not generate code or use reflective member access, but because it is
only useful alongside EF Core (which is non-AOT), the package as a whole should be treated as non-AOT.
If you call the annotated APIs from your own code, you will get the corresponding analyzer warnings —
that is expected; suppress them only in a non-AOT, non-trimmed deployment.

## Recommendations

- **AOT web app:** use `Tenantry.Core` + `Tenantry.AspNetCore`. For data, use a store and persistence
  approach that is itself AOT-friendly (e.g. a hand-written `ITenantStore` over an AOT-safe client).
  The tenant context, resolution, middleware, and access control are all AOT-safe.
- **EF Core app:** use the full stack and publish without Native AOT. Trimming is supported; test your
  trimmed build, as EF Core providers and your model may still need trim roots configured per EF Core's
  guidance.
