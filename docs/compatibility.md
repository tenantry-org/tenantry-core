# Compatibility

Which .NET versions, EF Core versions and databases Tenantry supports, and how its package dependencies are
declared.

## .NET versions

| .NET | Microsoft support | Tenantry | Supported until |
|------|-------------------|----------|-----------------|
| **.NET 10** | LTS, until 14 November 2028 | **Primary** | .NET 10's end of support |
| **.NET 11** | STS, two years from its release in November 2026 | **Primary once .NET 11 ships** (built and tested against the release candidate until then) | .NET 11's end of support |
| .NET 9 | STS, until 10 November 2026 | Legacy | 10 November 2027, one year after Microsoft's end of support |
| .NET 8 | LTS, until 10 November 2026 | Legacy | 10 November 2027, one year after Microsoft's end of support |

The packages target **net8.0, net9.0 and net10.0**; net11.0 is added when .NET 11 is released.

**Legacy** means the net8.0 and net9.0 builds are still shipped, built and tested, but Microsoft stops
patching .NET 8 and .NET 9, including EF Core 8 and 9, on 10 November 2026. Move to .NET 10.
They stay through the beta: 1.0 ends it, not before 10 November 2027, and drops them.

## EF Core

Each target framework's build of `Tenantry.EfCore` is compiled against that framework's EF Core major, and
accepts any later release of it:

| Target framework | EF Core |
|------------------|---------|
| net8.0 | 8.0.31 or later 8.x |
| net9.0 | 9.0.20 or later 9.x |
| net10.0 | 10.0.12 or later 10.x |

These minimums are the versions the tests run against. EF Core 9 on .NET 8 is not supported: each target
framework's build accepts, and is tested with, only that framework's EF Core major.

Three things Tenantry reads from EF Core are not documented by EF Core: the expressions of `ExecuteUpdate` setters,
the query behind `GetDatabaseValues()` and `Reload()`, and the name EF Core gives a failed transaction operation.
Tests pin each for every EF Core major above, and the weekly run takes the newest release of each major. If a release
changed one, Tenantry would reject every `ExecuteUpdate`, or refuse the commit after any failed transaction operation
that follows a save relying on a tenant check, but `GetDatabaseValues()` and `Reload()` would read another tenant's row
by its key, as they do without Tenantry.

## Databases

Tenantry uses only standard EF Core features, so it works with any relational EF Core provider that reports the rows
an `UPDATE` or `DELETE` matched: a forged write matches no row, which EF Core reports as a concurrency failure. These
combinations run the write-isolation suite (forged writes, entities loaded under another tenant, unchanged-value
updates, writes without a tenant, `ExecuteUpdate`/`ExecuteDelete` and the `TenantId` guard, `GetDatabaseValues` of
another tenant's row, pooled contexts, and a database per tenant) against a real database:

| Database | EF Core provider | Framework | Status |
|----------|------------------|-----------|--------|
| SQLite (in-memory) | `Microsoft.EntityFrameworkCore.Sqlite` | .NET 8, 9, 10 | Tested (unit suite) |
| SQL Server 2022 | `Microsoft.EntityFrameworkCore.SqlServer` 8.0.31, 9.0.20, 10.0.12 | .NET 8, 9, 10 | Tested |
| PostgreSQL 16 | `Npgsql.EntityFrameworkCore.PostgreSQL` 8.0.4, 9.0.0, 10.0.3 | .NET 8, 9, 10 | Tested |
| MySQL 8.4 | `Pomelo.EntityFrameworkCore.MySql` 8.0.2, 9.0.0 | .NET 8, 9 | Tested |
| MySQL 8.4 | `MySql.EntityFrameworkCore` (Oracle) 10.0.9 | .NET 10 | Tested |
| MySQL / MariaDB | `Pomelo.EntityFrameworkCore.MySql` | .NET 10 | Not tested (no EF Core 10 release) |

Each framework runs the suite with its own EF Core version. MariaDB is not tested.

Every build runs the suite against the versions in the table: the oldest release of each provider that the tests
allow, with the ADO.NET driver that provider requires at the least, against pinned server images (SQL Server 2022
CU27, PostgreSQL 16.15, MySQL 8.4.11). Each week two more runs report what has changed since:

- The newest release of each provider within its major, with the newest ADO.NET driver an application can update to:
  Npgsql and MySqlConnector in the major their provider supports, `Microsoft.Data.SqlClient` and `MySql.Data` at
  their newest release, against the pinned images.
- The pinned packages against the newest server releases: SQL Server 2025, the latest PostgreSQL and MySQL releases,
  and MySQL's long-term support release.

Those weekly runs find a break soon after a release; only the versions in the table run on every build.

Keep MySQL's default of reporting matched rows: with an option that reports changed rows (such as
`UseAffectedRows=true`), an update that changes no values reports zero rows and EF Core raises a false concurrency
failure. A save whose tenant check is another of its statements is kept all or nothing by rolling it back to a
savepoint, or rolling back its transaction, so on MySQL its tables must use a transactional engine such as InnoDB, the
default: a MyISAM table keeps the rows a failed save wrote.

With `string` tenant ids, the database compares them under the `TenantId` column's collation. SQL Server's and MySQL's
defaults ignore case, so the database takes `acme` and `ACME` for one tenant and each one's queries return the other's
rows. Every tenant's `string` id must be unique under that collation: a store keyed by the id in the same database
guarantees it, and `UseInMemoryStore` refuses ids that differ only in case, but not ones that differ only in accents.
See [String tenant ids and the database's collation](efcore-integration.md#string-tenant-ids-and-the-databases-collation).

## Native AOT and trimming

`Tenantry.Core`, `Tenantry.AspNetCore`, `Tenantry.Http`, `Tenantry.Caching` and `Tenantry.Options` support trimming
and Native AOT. `Tenantry.EfCore` supports trimming only, as EF Core does. See [AOT & trimming](aot-and-trimming.md).

## Dependency versions

- **`Microsoft.Extensions.*`**: a minimum from the target framework's own major (8.0 on net8.0), with no
  upper bound; `Tenantry.Caching` takes `Microsoft.Extensions.Caching.Abstractions` 9.0 or later on net8.0 too, the
  first with `HybridCache`. Microsoft ships every `Microsoft.Extensions` major for all supported frameworks, and current
  Azure SDKs need 10.x even on .NET 8.
- **EF Core**: the target framework's major only, as above.
- **Tenantry packages**: `Tenantry.EfCore`, `Tenantry.AspNetCore`, `Tenantry.Http`, `Tenantry.Caching` and
  `Tenantry.Options` take `Tenantry.Core` from their own release up to the next minor (`[x.y.z, x.(y+1).0)`),
  because a minor release may break the API before 1.0. Within a minor they can be updated separately.
- **Tenantry.Pro**: in the beta it releases each minor version with Tenantry Core's, and runs on that Core minor.

CI checks every minimum is a version the tests run against, and a weekly job runs the whole test suite with
every dependency at the newest version it allows.

## Supported versions

Security fixes go into the latest minor version, and, until 1.0, into the Tenantry Core minors that the latest two
Tenantry.Pro minors run on. Minor versions released before Tenantry.Pro goes on sale are not patched once a newer one
is out. Other fixes go only into the latest minor. See the
[security policy](../.github/SECURITY.md#supported-versions).
