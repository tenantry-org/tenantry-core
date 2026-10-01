using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tenantry.EfCore.Internal;

/// <summary>
/// The options extension <c>UseTenantry()</c> adds: it puts <see cref="TenantModelCustomizer"/> in place of EF Core's
/// model customizer.
/// </summary>
/// <remarks>
/// It holds no state, so every context that uses Tenantry can share one EF Core internal service provider: the
/// customizer and the interceptors find the tenant through each context's application service provider.
/// </remarks>
internal sealed class TenantryOptionsExtension : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    // Replace rather than TryAdd: this may run before or after the database provider registers its own customizer.
    public void ApplyServices(IServiceCollection services) =>
        services.Replace(ServiceDescriptor.Singleton<IModelCustomizer, TenantModelCustomizer>());

    // ReplaceService<IModelCustomizer, …>() is applied before the extensions, so this extension's customizer would
    // silently drop it (or, if it came last, the tenant filters would be lost). Refuse the combination instead.
    public void Validate(IDbContextOptions options)
    {
        var core = options.FindExtension<CoreOptionsExtension>();

        // EF Core applies no extension's services to an internal service provider the application builds itself, so
        // the tenant filters would be missing (and EF Core 10 also refuses Tenantry's query interceptor, a singleton
        // interceptor).
        if (core?.InternalServiceProvider is not null)
        {
            throw new InvalidOperationException(
                "UseTenantry() cannot be used with UseInternalServiceProvider: EF Core does not add Tenantry's model " +
                "customizer to an internal service provider you build, so the tenant query filters would be missing. " +
                "Remove UseInternalServiceProvider and let EF Core build its own.");
        }

        var replaced = core?.ReplacedServices;

        if (replaced?.FirstOrDefault(service => service.Key.Item1 == typeof(IModelCustomizer)) is { Value: { } customizer })
        {
            throw new InvalidOperationException(
                $"This context replaces EF Core's IModelCustomizer with '{customizer.Name}', but UseTenantry() adds the " +
                "tenant query filters through its own model customizer, so one of the two would be lost. Move that " +
                "configuration into OnModelCreating, or into an ITenantModelContributor, which Tenantry's customizer runs.");
        }
    }

    private sealed class ExtensionInfo(IDbContextOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => false;

        public override string LogFragment => "using Tenantry ";

        public override int GetServiceProviderHashCode() => 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) => other is ExtensionInfo;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) => debugInfo["Tenantry"] = "1";
    }
}
