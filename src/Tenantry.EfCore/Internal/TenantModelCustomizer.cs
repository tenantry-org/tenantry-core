using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Tenantry.EfCore.Internal;

/// <summary>
/// EF Core's model customizer, which runs the context's <c>OnModelCreating</c>, followed by every
/// <see cref="ITenantModelContributor"/> and then the tenant query filters, so no configuration can come after them.
/// </summary>
internal sealed class TenantModelCustomizer(ModelCustomizerDependencies dependencies) : RelationalModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);

        var services = ApplicationServices.Find(context);

        foreach (var contributor in services?.GetServices<ITenantModelContributor>() ?? [])
        {
            contributor.Configure(modelBuilder, context);
        }

        TenantIsolation.ForModel(modelBuilder.Model)?.ConfigureModel(modelBuilder, context, services);
    }
}
