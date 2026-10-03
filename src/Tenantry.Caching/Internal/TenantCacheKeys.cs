using System.Text;

namespace Tenantry.Caching.Internal;

/// <summary>
/// How a tenant's entries are named in the cache that <c>IsolateCaches()</c> wraps. A tenant's keys and tags start
/// with <c>t:&lt;id&gt;:</c>, every tenant entry is also tagged <c>t:&lt;id&gt;</c> (the tenant) and <c>t:</c> (every
/// tenant), and <see cref="SharedHybridCache"/>'s keys and tags start with <c>s:</c>, with <c>s:</c> on every shared
/// entry, so tenants' entries and shared ones never share a name. Ids are never empty (Tenantry reserves an empty id),
/// so a tenant's tag is never <c>t:</c>. In the id, <c>\</c> and <c>:</c> are escaped, so <c>t:a:b:c</c> cannot be read as both
/// tenant <c>a</c>'s key <c>b:c</c> and tenant <c>a:b</c>'s key <c>c</c>.
/// </summary>
internal static class TenantCacheKeys
{
    public const string AllTenantsTag = "t:";

    /// <summary>The tag every shared entry has, which <see cref="EveryEntry"/> removes from the shared cache.</summary>
    public const string AllSharedTag = "s:";

    /// <summary>HybridCache's tag for every entry, which each wrapper narrows to its own.</summary>
    public const string EveryEntry = "*";

    private const string SharedPrefix = "s:";

    public static string TenantTag(string tenantId) => $"t:{Escape(tenantId)}";

    public static string TenantPrefix(string tenantId) => $"t:{Escape(tenantId)}:";

    public static string Shared(string keyOrTag)
    {
        ArgumentNullException.ThrowIfNull(keyOrTag);
        return SharedPrefix + keyOrTag;
    }

    private static string Escape(string tenantId)
    {
        if (tenantId.AsSpan().IndexOfAny('\\', ':') < 0)
            return tenantId;

        StringBuilder escaped = new(tenantId.Length + 4);
        foreach (var c in tenantId)
        {
            if (c is '\\' or ':')
                escaped.Append('\\');
            escaped.Append(c);
        }

        return escaped.ToString();
    }
}
