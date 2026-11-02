# Tenantry samples

Six applications that use Tenantry's free packages. Each runs with `dotnet run` from the repository root and needs no
database server: they keep their data in SQLite files or in memory. The repository's `global.json` names the .NET 11
SDK, and the samples target `net10.0`, so they also need the .NET 10 runtime
([Contributing](../CONTRIBUTING.md#prerequisites)).

| Sample | Demonstrates |
|--------|--------------|
| [`SecureApi`](Tenantry.Samples.SecureApi) | Production starting point: JWT authentication, tenant selection validated against the caller's claims, required tenants, EF Core isolation, integration tests |
| [`Quickstart`](Tenantry.Samples.Quickstart) | Minimal ASP.NET Core setup, resolvers, access validators, endpoint metadata |
| [`EfCoreWeb`](Tenantry.Samples.EfCoreWeb) | An EF Core app with migrations, DB-backed store, mixed tenanted/global entities, admin queries |
| [`EfCoreConsole`](Tenantry.Samples.EfCoreConsole) | EF Core isolation with no ASP.NET Core, using `AddTenantry` and manual scopes |
| [`DatabasePerTenant`](Tenantry.Samples.DatabasePerTenant) | A database per tenant with `UseConnectionStrings` and a pooled `AddDbContextPerTenantDatabase`, plus worker scopes |
| [`Aot`](Tenantry.Samples.Aot) | Native-AOT-published ASP.NET Core app: header, subdomain and custom resolvers, problem details |

The quickstarts, `EfCoreWeb` and `Aot` take the tenant from a header and do not authenticate callers, so any caller can
act as any tenant. `SecureApi` shows how to check the tenant against the caller's token.

## SecureApi

Listens on port 5194. Get a token for tenant `acme` from its development endpoint, then call the API with it:

```bash
dotnet run --project samples/Tenantry.Samples.SecureApi

curl -X POST localhost:5194/dev/token -H 'Content-Type: application/json' \
     -d '{"subject":"alice","tenants":["acme"]}'
curl localhost:5194/notes -H 'Authorization: Bearer <token>' -H 'X-Tenant-Id: acme'
```

The same request without the token gets 401, and with `X-Tenant-Id: globex` gets 403. The `tenantry-api` template
creates a project from this sample (`dotnet new install Tenantry.Templates`, then `dotnet new tenantry-api`).

## Quickstart

Listens on port 5172. `Tenantry.Samples.Quickstart.http` has more requests.

```bash
dotnet run --project samples/Tenantry.Samples.Quickstart

curl localhost:5172/orders -H 'X-Tenant-Id: acme'
```

## EfCoreWeb

Listens on port 5181. It applies its migrations to `efcore-sample.db` as it starts. The header comment in `Program.cs`
lists more requests, including the admin endpoint that reads every tenant's orders.

```bash
dotnet run --project samples/Tenantry.Samples.EfCoreWeb

curl localhost:5181/products
curl localhost:5181/orders -H 'X-Tenant-Id: acme'
```

## EfCoreConsole

Runs its steps as two tenants, prints what each sees, and exits.

```bash
dotnet run --project samples/Tenantry.Samples.EfCoreConsole
```

## DatabasePerTenant

Gives each tenant its own SQLite file, prints what each sees, and exits.

```bash
dotnet run --project samples/Tenantry.Samples.DatabasePerTenant
```

## Aot

Listens on port 5268, whether started with `dotnet run` or published with Native AOT and run from its publish folder
([Native AOT and trimming](../docs/aot-and-trimming.md)). `Tenantry.Samples.Aot.http` has more requests, by header,
subdomain and cookie.

```bash
dotnet run --project samples/Tenantry.Samples.Aot

curl localhost:5268/orders -H 'X-Tenant-Id: acme'
```
