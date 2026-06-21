using Tenantry.Core;

namespace Tenantry.EfCore.Tests.Infrastructure;

/// <summary>
/// Mutable tenant context for use in unit tests.
/// </summary>
/// <remarks>
/// <para>
/// Uses a <see langword="static"/> <see cref="AsyncLocal{T}"/> so that ALL instances of
/// this class within the same async context share the same current tenant value. This is
/// necessary because EF Core caches the compiled model (including query-filter closures)
/// per <c>DbContext</c> type. If the model's closure captured an earlier instance, that
/// instance still correctly reflects the current tenant because all instances read from
/// the same <see cref="AsyncLocal{T}"/>.
/// </para>
/// <para>
/// xUnit runs each <c>[Fact]</c> in its own <see cref="AsyncLocal{T}"/> context, so
/// tests running concurrently do not interfere with each other.
/// </para>
/// </remarks>
public sealed class TestTenantContext : ITenantContext<string>
{
    // Shared across all instances within the same async execution context.
    private static readonly AsyncLocal<string?> CurrentTenantLocal = new();

    /// <inheritdoc />
    public ITenantDescriptor<string>? CurrentTenant =>
        CurrentTenantLocal.Value is { } id
            ? new TenantDescriptor<string> { TenantId = id, Name = id }
            : null;

    /// <inheritdoc />
    public bool HasTenant => CurrentTenantLocal.Value is not null;

    /// <inheritdoc />
    public string? CurrentTenantId => CurrentTenantLocal.Value;

    /// <summary>Sets the current tenant to the given ID and returns this instance.</summary>
    public TestTenantContext As(string tenantId)
    {
        CurrentTenantLocal.Value = tenantId;
        return this;
    }

    /// <summary>Clears the current tenant and returns this instance.</summary>
    public TestTenantContext AsNone()
    {
        CurrentTenantLocal.Value = null;
        return this;
    }

    /// <summary>Creates a new context pre-set to the given tenant.</summary>
    public static TestTenantContext For(string tenantId) =>
        new TestTenantContext().As(tenantId);

    /// <summary>Creates a new context with no tenant resolved.</summary>
    public static TestTenantContext Empty() => new();
}

/// <summary>
/// Mutable <see cref="Guid"/>-keyed tenant context for use in unit tests.
/// </summary>
/// <remarks>
/// Uses a <see langword="static"/> <see cref="AsyncLocal{T}"/> so that all instances within
/// the same async context share the same current tenant — required by EF Core's compiled model cache.
/// xUnit runs each <c>[Fact]</c> in its own async context, so tests running concurrently
/// do not interfere with each other.
/// </remarks>
public sealed class GuidTestTenantContext : ITenantContext<Guid>
{
    // Store ITenantDescriptor so HasTenant can distinguish "not set" from Guid.Empty.
    private static readonly AsyncLocal<ITenantDescriptor<Guid>?> CurrentTenantLocal = new();

    /// <inheritdoc />
    public ITenantDescriptor<Guid>? CurrentTenant => CurrentTenantLocal.Value;

    /// <inheritdoc />
    public bool HasTenant => CurrentTenantLocal.Value is not null;

    /// <inheritdoc />
    /// <remarks>
    /// For unconstrained value-type TKey, the interface's <c>TKey?</c> erases to <c>TKey</c>
    /// at CLR level (not <c>Nullable&lt;TKey&gt;</c>). Returns <c>default</c> when no tenant
    /// is set; check <see cref="HasTenant"/> to distinguish from a legitimate empty Guid.
    /// </remarks>
    public Guid CurrentTenantId => CurrentTenantLocal.Value?.TenantId ?? Guid.Empty;

    /// <summary>Sets the current tenant to the given ID and returns this instance.</summary>
    public GuidTestTenantContext As(Guid tenantId)
    {
        CurrentTenantLocal.Value = new TenantDescriptor<Guid> { TenantId = tenantId, Name = tenantId.ToString() };
        return this;
    }

    /// <summary>Clears the current tenant and returns this instance.</summary>
    public GuidTestTenantContext AsNone()
    {
        CurrentTenantLocal.Value = null;
        return this;
    }
}
