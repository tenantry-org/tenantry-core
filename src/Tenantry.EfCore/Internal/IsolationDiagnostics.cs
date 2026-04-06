namespace Tenantry.EfCore.Internal;

/// <summary>
/// Structured diagnostic information for a tenant isolation violation.
/// </summary>
internal sealed class IsolationDiagnostics
{
    /// <summary>CLR type name of the violating entity.</summary>
    public required string EntityTypeName { get; init; }

    /// <summary>The TenantId found on the offending entity.</summary>
    public required string OffendingTenantId { get; init; }

    /// <summary>The TenantId expected by the current scope.</summary>
    public required string ExpectedTenantId { get; init; }

    /// <summary>UTC timestamp when the violation was detected.</summary>
    public DateTimeOffset DetectedAt { get; init; } = DateTimeOffset.UtcNow;
}
