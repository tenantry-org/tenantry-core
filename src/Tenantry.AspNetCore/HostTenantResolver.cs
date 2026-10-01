using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Http;

namespace Tenantry.AspNetCore;

/// <summary>
/// Resolves the tenant from the request's host name, for tenants that bring their own domain: a request to
/// <c>app.acme.com</c> has the identifier <c>app.acme.com</c>, which your tenant store's
/// <see cref="ITenantStore{TKey}.FindByIdentifierAsync"/> maps to a tenant.
/// </summary>
/// <remarks>
/// <para>
/// The host name is returned in lower case, without a port or a trailing dot, and an international domain name in
/// its ASCII form (<c>xn--…</c>), as DNS has it. A host that is an IP address (a load balancer's or Kubernetes' health
/// probe), or is or is under one of <see cref="HostTenantResolverOptions.ExcludedDomains"/> (<c>localhost</c> by
/// default), resolves nothing.
/// </para>
/// <para>
/// Every other host resolves, so resolvers added after it never run for those hosts: add it last. Exclude your own
/// domain, whose subdomains <c>ResolveFromSubdomain</c> resolves, so its hosts (<c>www</c>, the apex) do not ask the
/// tenant store for a tenant on every request.
/// </para>
/// </remarks>
public sealed class HostTenantResolver : ITenantResolver
{
    private readonly string[] _excludedDomains;

    /// <summary>
    /// Creates a resolver with the default options.
    /// </summary>
    public HostTenantResolver()
        : this(new HostTenantResolverOptions())
    {
    }

    /// <summary>
    /// Creates a resolver with the given options, copied when it is created.
    /// </summary>
    /// <param name="options">The domains whose hosts are not tenants.</param>
    public HostTenantResolver(HostTenantResolverOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _excludedDomains = [.. HostNames.Domains(options.ExcludedDomains)];
    }

    /// <inheritdoc />
    public ValueTask<string?> ResolveAsync(HttpContext context, CancellationToken cancellationToken = default)
    {
        var host = HostNames.Of(context);

        return new ValueTask<string?>(host is null || _excludedDomains.Any(domain => HostNames.IsUnder(host, domain)) ? null : host);
    }
}

/// <summary>
/// Options for <see cref="HostTenantResolver"/>, set with <c>tenant.ResolveFromHost(o =&gt; …)</c>.
/// </summary>
public sealed class HostTenantResolverOptions
{
    /// <summary>
    /// Domains whose hosts are not tenants: a host that is one of them, or a subdomain of one, resolves nothing.
    /// Contains <c>localhost</c> by default. Add your own domain, such as <c>example.com</c>, whose subdomains
    /// <c>ResolveFromSubdomain</c> resolves.
    /// </summary>
    public ISet<string> ExcludedDomains { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "localhost" };
}

/// <summary>
/// The request's host name as the host resolvers compare it: lower case, without a port or a trailing dot, and an
/// international domain name in its ASCII form.
/// </summary>
internal static class HostNames
{
    private static readonly IdnMapping Idn = new();

    // Null for no host, or for an IP address.
    public static string? Of(HttpContext context)
    {
        var host = Normalize(context.Request.Host.Host);

        return host is null || IPAddress.TryParse(host, out _) ? null : host;
    }

    public static IEnumerable<string> Domains(IEnumerable<string> domains) =>
        domains
            .Select(domain => Normalize(domain.Trim().Trim('.')))
            .OfType<string>();

    // Whether host is domain, or a subdomain of it.
    public static bool IsUnder(string host, string domain) =>
        host.Length == domain.Length
            ? host == domain
            : host.Length > domain.Length && host[^(domain.Length + 1)] == '.' && host.EndsWith(domain, StringComparison.Ordinal);

    // ASP.NET Core decodes an international domain name to Unicode; DNS, certificates and configuration usually have
    // its ASCII form, so both are compared in ASCII.
    private static string? Normalize(string host)
    {
        host = host.TrimEnd('.');

        if (host.Length == 0)
        {
            return null;
        }

        if (!host.All(char.IsAscii))
        {
            try
            {
                host = Idn.GetAscii(host);
            }
            catch (ArgumentException)
            {
                // Not a valid international domain name: compare it as it is.
            }
        }

        return host.ToLowerInvariant();
    }
}
