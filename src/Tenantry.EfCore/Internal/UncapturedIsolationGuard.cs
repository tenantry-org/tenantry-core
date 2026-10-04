using Microsoft.EntityFrameworkCore;

namespace Tenantry.EfCore.Internal;

/// <summary>
/// Added by <c>UseTenantry()</c> when its options had no application services yet: refuses the context before each
/// connection, command and save when its application checks unmarked entity types
/// (<see cref="TenantModelCheck.ThrowIfUncaptured"/>). A query EF Core serves from its cache is not compiled again, so
/// the query interceptor alone would not see it; its command is.
/// </summary>
internal sealed class UncapturedIsolationGuard : TenantContextGuard
{
    public static readonly UncapturedIsolationGuard Instance = new();

    private UncapturedIsolationGuard()
    {
    }

    protected override void Check(DbContext context) => TenantModelCheck.ThrowIfUncaptured(context);
}
