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

These minimums are the versions the tests run against. EF Core 9 on .NET 8 is not supported: use the EF
Core that matches your target framework, as you would in any EF Core application.

## Databases

Tenantry uses only standard EF Core features, so it works with any relational EF Core provider that reports
the rows an `UPDATE` or `DELETE` matched. The write-isolation suite runs against:

| Database | EF Core provider | .NET |
|----------|------------------|------|
| SQLite | `Microsoft.EntityFrameworkCore.Sqlite` | 8, 9, 10 |
| SQL Server 2022 | `Microsoft.EntityFrameworkCore.SqlServer` | 10 |
| PostgreSQL 16 | `Npgsql.EntityFrameworkCore.PostgreSQL` | 10 |
| MySQL 8.4 | `MySql.EntityFrameworkCore` (Oracle) | 10 |

Details and caveats are in [Tested providers](efcore-integration.md#tested-providers).

## Native AOT and trimming

`Tenantry.Core` and `Tenantry.AspNetCore` are trim- and Native AOT-compatible; `Tenantry.EfCore` is not,
because EF Core is not. See [AOT & trimming](aot-and-trimming.md).

## Dependency versions

- **`Microsoft.Extensions.*`**: a minimum from the target framework's own major (8.0 on net8.0), with no
  upper bound. Microsoft ships every `Microsoft.Extensions` major for all supported frameworks, and current
  Azure SDKs need 10.x even on .NET 8.
- **EF Core**: the target framework's major only, as above.
- **Tenantry packages**: `Tenantry.EfCore` and `Tenantry.AspNetCore` need exactly the same version of
  `Tenantry.Core`. Update them together.

CI checks every minimum is a version the tests run against, and a weekly job runs the whole test suite with
every dependency at the newest version it allows.
