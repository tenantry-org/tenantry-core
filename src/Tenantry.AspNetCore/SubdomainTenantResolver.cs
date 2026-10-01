using Microsoft.AspNetCore.Http;

namespace Tenantry.AspNetCore;

/// <summary>
/// Resolves the tenant from the subdomain of the request host. For example, <c>acme.app.example.com</c>
/// resolves to <c>acme</c>.
/// </summary>
/// <remarks>
/// <para>
/// Without <see cref="SubdomainTenantResolverOptions.BaseDomains"/>, the first label of a host with at least
/// three labels is the tenant: <c>acme.example.com</c> resolves to <c>acme</c>, and <c>example.com</c>,
/// <c>localhost</c> and <c>acme.localhost</c> resolve nothing. With them, only a host of exactly one label followed
/// by a base domain resolves: with <c>example.com</c>, <c>acme.example.com</c> resolves to <c>acme</c>, while
/// <c>example.com</c>, <c>other.org</c> and <c>x.acme.example.com</c> resolve nothing.
/// </para>
/// <para>
/// A subdomain in <see cref="SubdomainTenantResolverOptions.IgnoredSubdomains"/> (<c>www</c> by default) and a
/// host that is an IP address resolve nothing. The subdomain is returned in lower case, as host names are compared
/// without regard to case, and an international domain name is compared and returned in its ASCII form (<c>xn--…</c>).
/// </para>
/// </remarks>
public sealed class SubdomainTenantResolver : ITenantResolver
{
    private readonly string[] _baseDomainSuffixes;
    private readonly HashSet<string> _ignoredSubdomains;

    /// <summary>
    /// Creates a resolver with the default options.
    /// </summary>
    public SubdomainTenantResolver()
        : this(new SubdomainTenantResolverOptions())
    {
    }

    /// <summary>
    /// Creates a resolver with the given options, copied when it is created.
    /// </summary>
    /// <param name="options">The base domains and the subdomains to ignore.</param>
    public SubdomainTenantResolver(SubdomainTenantResolverOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _baseDomainSuffixes = [.. HostNames.Domains(options.BaseDomains).Select(domain => "." + domain)];
        _ignoredSubdomains = new HashSet<string>(HostNames.Domains(options.IgnoredSubdomains), StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public ValueTask<string?> ResolveAsync(HttpContext context, CancellationToken cancellationToken = default)
    {
        var subdomain = Subdomain(HostNames.Of(context));

        return new ValueTask<string?>(
            string.IsNullOrWhiteSpace(subdomain) || _ignoredSubdomains.Contains(subdomain) ? null : subdomain);
    }

    private string? Subdomain(string? host)
    {
        if (host is null)
        {
            return null;
        }

        if (_baseDomainSuffixes.Length > 0)
        {
            foreach (var suffix in _baseDomainSuffixes)
            {
                if (host.EndsWith(suffix, StringComparison.Ordinal) && host.Length > suffix.Length)
                {
                    var label = host[..^suffix.Length];

                    if (!label.Contains('.'))
                    {
                        return label;
                    }
                }
            }

            return null;
        }

        var labels = host.Split('.');
        return labels.Length < 3 ? null : labels[0];
    }
}

/// <summary>
/// Options for <see cref="SubdomainTenantResolver"/>, set with <c>tenant.ResolveFromSubdomain(o =&gt; …)</c>.
/// </summary>
public sealed class SubdomainTenantResolverOptions
{
    /// <summary>
    /// The domains whose subdomains are tenants, such as <c>example.com</c> for <c>acme.example.com</c>, and
    /// <c>localhost</c> for <c>acme.localhost</c> in development. When set, only a host of exactly one label followed
    /// by one of them resolves a tenant. When empty (the default), the first label of any host with at least three
    /// labels is the tenant.
    /// </summary>
    public ISet<string> BaseDomains { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Subdomains that are never tenants, compared without regard to case. Contains <c>www</c> by default; add
    /// others, such as <c>api</c> or <c>app</c>, that serve the application itself.
    /// </summary>
    public ISet<string> IgnoredSubdomains { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "www" };
}
