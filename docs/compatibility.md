# Compatibility

The .NET versions, EF Core versions and databases Tenantry supports, and how its package dependencies are declared.

## .NET versions

| .NET | Microsoft support | Tenantry | Supported until |
|------|-------------------|----------|-----------------|
| .NET 10 | LTS, until 14 November 2028 | Primary | .NET 10's end of support |
| .NET 11 | STS, two years from its release in November 2026 | Primary once .NET 11 ships (built and tested against the release candidate until then) | .NET 11's end of support |
| .NET 9 | STS, until 10 November 2026 | Legacy | 10 November 2027, one year after Microsoft's end of support |
| .NET 8 | LTS, until 10 November 2026 | Legacy | 10 November 2027, one year after Microsoft's end of support |

Move to .NET 10: Microsoft stops patching .NET 8 and .NET 9, EF Core 8 and 9 included, on 10 November 2026. Legacy
means the net8.0 and net9.0 builds are still shipped, built and tested. They stay through the beta. 1.0 ends the beta
and drops them, and comes no earlier than 10 November 2027.

The packages target net8.0, net9.0 and net10.0. net11.0 is added when .NET 11 is released.

## EF Core

Each target framework's build of `Tenantry.EfCore` accepts only that framework's EF Core major, from the release below,
and is compiled and tested against it. EF Core 9 on .NET 8 is not supported.

| Target framework | EF Core |
|------------------|---------|
| net8.0 | 8.0.31 or later 8.x |
| net9.0 | 9.0.20 or later 9.x |
| net10.0 | 10.0.12 or later 10.x |

The tests run against these minimums.

## Databases

Tenantry works with any relational EF Core provider that reports the rows an `UPDATE` or `DELETE` matched, as it uses
only standard EF Core features. A forged write matches no row, which EF Core reports as a concurrency failure.

These combinations run the write-isolation suite against a real database on every build:

| Database | EF Core provider | Framework | Status |
|----------|------------------|-----------|--------|
| SQLite (in-memory) | `Microsoft.EntityFrameworkCore.Sqlite` | .NET 8, 9, 10 | Tested (unit suite) |
| SQL Server 2022 | `Microsoft.EntityFrameworkCore.SqlServer` 8.0.31, 9.0.20, 10.0.12 | .NET 8, 9, 10 | Tested |
| PostgreSQL 16 | `Npgsql.EntityFrameworkCore.PostgreSQL` 8.0.4, 9.0.0, 10.0.3 | .NET 8, 9, 10 | Tested |
| MySQL 8.4 | `Pomelo.EntityFrameworkCore.MySql` 8.0.2, 9.0.0 | .NET 8, 9 | Tested |
| MySQL 8.4 | `MySql.EntityFrameworkCore` (Oracle) 10.0.9 | .NET 10 | Tested |
| MySQL / MariaDB | `Pomelo.EntityFrameworkCore.MySql` | .NET 10 | Not tested (no EF Core 10 release) |

Each framework runs the suite with its own EF Core version. MariaDB is not tested.

On MySQL:

- Keep the default of reporting matched rows. With an option that reports changed rows, such as
  `UseAffectedRows=true`, an update that changes no values reports zero rows. EF Core then raises a false concurrency
  failure.
- Use a transactional engine such as InnoDB, the default. A save whose tenant check is another of its statements is
  undone by rolling it back, and a MyISAM table keeps the rows it wrote
  ([Saves that succeed or fail as a whole](efcore-advanced.md#saves-that-succeed-or-fail-as-a-whole)).

With `string` tenant ids, every tenant's id must be unique under the `TenantId` column's collation, as the database
compares ids under it. SQL Server's and MySQL's defaults ignore case
([String tenant ids and the database's collation](efcore-integration.md#string-tenant-ids-and-the-databases-collation)).

## Dependency versions

- `Microsoft.Extensions.*` packages take a minimum from the target framework's own major (8.0 on net8.0), with no
  upper bound. Microsoft ships every `Microsoft.Extensions` major for all supported frameworks, and current Azure SDKs
  need 10.x even on .NET 8.
- `Tenantry.Caching` takes `Microsoft.Extensions.Caching.Abstractions` 9.0 or later on net8.0 too, the first with
  `HybridCache`.
- EF Core is taken in the target framework's major only, as above.
- `Tenantry.EfCore`, `Tenantry.AspNetCore`, `Tenantry.Http`, `Tenantry.Caching` and `Tenantry.Options` take
  `Tenantry.Core` from their own release up to the next minor (`[x.y.z, x.(y+1).0)`), as a minor release may break
  the API before 1.0. Within a minor they can be updated separately.
- In the beta, Tenantry.Pro releases each minor version with Tenantry Core's, and runs on that Core minor.

CI checks that every minimum is a version the tests run against. A weekly job runs the whole test suite with every
dependency at the newest version it allows.

## Supported versions

Fixes other than security fixes go only into the latest minor version. Security fixes go into it too, and until 1.0
into some older Tenantry Core minors: the [security policy](../.github/SECURITY.md#supported-versions) says which.

## Details

### What Tenantry reads from EF Core

EF Core does not document three things Tenantry reads from it. Tests pin each for every EF Core major above, and the
weekly run takes the newest release of each major. If a release changed one:

- The expressions of `ExecuteUpdate` setters: Tenantry would reject every `ExecuteUpdate`.
- The name EF Core gives a failed transaction operation: Tenantry would refuse the commit after any failed transaction
  operation that follows a save relying on a tenant check.
- The query behind `GetDatabaseValues()` and `Reload()`: they would read another tenant's row by its key, as they do
  without Tenantry.

### The write-isolation suite

The suite covers forged writes, entities loaded under another tenant, unchanged-value updates, writes without a tenant,
`ExecuteUpdate`/`ExecuteDelete` and the `TenantId` guard, `GetDatabaseValues` of another tenant's row, pooled contexts,
and a database per tenant.

Every build runs it against the versions in the table: the oldest release of each provider that the tests allow, with
the ADO.NET driver that provider requires at the least. The server images are pinned: SQL Server 2022 CU27,
PostgreSQL 16.15 and MySQL 8.4.11. Only these versions run on every build.

Each week two more runs report what has changed since, so they find a break soon after a release:

- The newest release of each provider within its major, against the pinned images. It runs with the newest ADO.NET
  driver an application can update to: Npgsql and MySqlConnector in the major their provider supports,
  `Microsoft.Data.SqlClient` and `MySql.Data` at their newest release.
- The pinned packages against the newest server releases: SQL Server 2025, the latest PostgreSQL and MySQL releases,
  and MySQL's long-term support release.
