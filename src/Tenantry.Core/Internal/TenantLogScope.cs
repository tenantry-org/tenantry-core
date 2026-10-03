using System.Collections;

namespace Tenantry.Internal;

/// <summary>
/// The log scope a tenant opens: one property, <see cref="TenantTelemetry.LogScopeName"/>.
/// </summary>
internal sealed class TenantLogScope(string tenantId) : IReadOnlyList<KeyValuePair<string, object?>>
{
    public int Count => 1;

    public KeyValuePair<string, object?> this[int index] =>
        index == 0
            ? new KeyValuePair<string, object?>(TenantTelemetry.LogScopeName, tenantId)
            : throw new ArgumentOutOfRangeException(nameof(index));

    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
    {
        yield return this[0];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() => $"{TenantTelemetry.LogScopeName}:{tenantId}";
}
