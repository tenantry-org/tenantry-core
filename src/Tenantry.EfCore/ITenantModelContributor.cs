using System.ComponentModel;
using Microsoft.EntityFrameworkCore;

namespace Tenantry.EfCore;

/// <summary>
/// Adds to the model of every <see cref="DbContext"/> that uses <c>UseTenantry()</c>.
/// </summary>
/// <remarks>
/// Register implementations in the application's service collection, as singletons. Contributors run while EF Core
/// builds the model, after the context's <c>OnModelCreating</c> and before Tenantry adds its tenant query filters, so
/// an entity type a contributor adds is isolated too. EF Core builds a model once and caches it, by default once per
/// context type. They are resolved from the context's application service provider: a context built without it, such
/// as by a design-time factory that builds its options by hand, gets a model without their contributions.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public interface ITenantModelContributor
{
    /// <summary>
    /// Configures a context's model.
    /// </summary>
    /// <param name="modelBuilder">The model builder, after <c>OnModelCreating</c>.</param>
    /// <param name="context">The context the model is being built for.</param>
    void Configure(ModelBuilder modelBuilder, DbContext context);
}
