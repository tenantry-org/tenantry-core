using Microsoft.EntityFrameworkCore;

namespace Tenantry.EfCore;

/// <summary>
/// Adds to the options of every <see cref="DbContext"/> that uses <c>UseTenantry()</c>. Register implementations in
/// the application's service collection, as singletons; <c>UseTenantry()</c> applies each of them.
/// </summary>
/// <remarks>
/// Packages that build on Tenantry use this to configure contexts without asking the application to add another
/// call to each one, for example to add an interceptor. Contributors run when <c>UseTenantry()</c> is called with the
/// application service provider in place, as it is inside <c>AddDbContext</c>, <c>AddDbContextPool</c>,
/// <c>AddDbContextFactory</c> and <c>AddPooledDbContextFactory</c>. A pooled context's options are built once, so a
/// contributor must not depend on the current tenant.
/// </remarks>
public interface ITenantDbContextOptionsContributor
{
    /// <summary>
    /// Configures a context's options. <c>optionsBuilder.Options.ContextType</c> is the context being configured.
    /// </summary>
    /// <param name="optionsBuilder">The options builder <c>UseTenantry()</c> was called on.</param>
    void Configure(DbContextOptionsBuilder optionsBuilder);
}
