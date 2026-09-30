using System.Net;
using Microsoft.AspNetCore.Http;

namespace Tenantry.AspNetCore;

/// <summary>
/// Resolves the tenant from the subdomain of the request host. For example, <c>acme.app.example.com</c>
/// resolves to <c>acme</c>.
/// </summary>
/// <remarks>
/// <para>
/// Without <see cref="SubdomainTenantResolverOptions.BaseDomain"/>, the first label of a host with at least
/// three labels is the tenant: <c>acme.example.com</c> resolves to <c>acme</c>, and <c>example.com</c>,
/// <c>localhost</c> and <c>acme.localhost</c> resolve nothing. With it, only a host of exactly one label followed
/// by the base domain resolves: with <c>example.com</c>, <c>acme.example.com</c> resolves to <c>acme</c>, while
/// <c>example.com</c>, <c>other.org</c> and <c>x.acme.example.com</c> resolve nothing.
/// </para>
/// <para>
/// A subdomain in <see cref="SubdomainTenantResolverOptions.IgnoredSubdomains"/> (<c>www</c> by default) and a
/// host that is an IP address resolve nothing.
/// </para>
/// </remarks>
public sealed class SubdomainTenantResolver : ITenantResolver
{
    private readonly string? _baseDomainSuffix;
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
    /// <param name="options">The base domain and the subdomains to ignore.</param>
    public SubdomainTenantResolver(SubdomainTenantResolverOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var baseDomain = options.BaseDomain?.Trim().Trim('.');
        _baseDomainSuffix = string.IsNullOrEmpty(baseDomain) ? null : "." + baseDomain;
        _ignoredSubdomains = new HashSet<string>(options.IgnoredSubdomains, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public ValueTask<string?> ResolveAsync(HttpContext context, CancellationToken cancellationToken = default)
    {
        var subdomain = Subdomain(context.Request.Host.Host);

        return new ValueTask<string?>(
            string.IsNullOrWhiteSpace(subdomain) || _ignoredSubdomains.Contains(subdomain) ? null : subdomain);
    }

    private string? Subdomain(string host)
    {
        if (string.IsNullOrEmpty(host) || IPAddress.TryParse(host, out _))
        {
            return null;
        }

        if (_baseDomainSuffix is not null)
        {
            if (!host.EndsWith(_baseDomainSuffix, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var label = host[..^_baseDomainSuffix.Length];
            return label.Contains('.') ? null : label;
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
    /// The domain whose subdomains are tenants, such as <c>example.com</c> for <c>acme.example.com</c>, or
    /// <c>localhost</c> for <c>acme.localhost</c> in development. When set, only a host of exactly one label
    /// followed by this domain resolves a tenant. When not set, the first label of any host with at least three
    /// labels is the tenant.
    /// </summary>
    public string? BaseDomain { get; set; }

    /// <summary>
    /// Subdomains that are never tenants, compared without regard to case. Contains <c>www</c> by default; add
    /// others, such as <c>api</c> or <c>app</c>, that serve the application itself.
    /// </summary>
    public ISet<string> IgnoredSubdomains { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "www" };
}
