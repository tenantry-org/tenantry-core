using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tenantry.EfCore.Internal;

/// <summary>
/// The options extension <c>UseTenantry()</c> adds: it puts <see cref="TenantModelCustomizer"/> in place of EF Core's
/// model customizer.
/// </summary>
/// <remarks>
/// Its only state is the isolation options the context follows, which change no EF Core service. Each context reads
/// them from its own options on every save, so contexts can share an internal service provider whatever their
/// <c>OnMissingTenant</c> and <c>OnSaveWithoutTransaction</c>. <c>OnUnclassifiedEntityType</c> is different: Tenantry
/// applies it when EF Core compiles a query, and a query served from the provider's query cache is not compiled again.
/// So it is part of the provider's key, and a context never runs a query another context compiled under another value.
/// </remarks>
internal sealed class TenantryOptionsExtension(EfCoreIsolationOptions? isolation = null) : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

    /// <summary>
    /// The isolation options the context follows: those <c>UseTenantry(configure)</c> set, or the application's as they
    /// were when <c>UseTenantry()</c> ran; null when the options had no application service provider then.
    /// </summary>
    public EfCoreIsolationOptions? Isolation { get; } = isolation;

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

        private EfCoreIsolationOptions? Isolation => ((TenantryOptionsExtension)Extension).Isolation;

        public override int GetServiceProviderHashCode() => (int?)Isolation?.OnUnclassifiedEntityType ?? -1;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) =>
            other is ExtensionInfo info && Same(Isolation, info.Isolation);

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
        {
            debugInfo["Tenantry"] = "1";

            if (Isolation is { } isolation)
            {
                debugInfo["Tenantry:OnUnclassifiedEntityType"] = isolation.OnUnclassifiedEntityType.ToString();
            }
        }

        private static bool Same(EfCoreIsolationOptions? left, EfCoreIsolationOptions? right) =>
            left?.OnUnclassifiedEntityType == right?.OnUnclassifiedEntityType;
    }
}
