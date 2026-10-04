using Tenantry.AspNetCore.Analyzers;

namespace Tenantry.Analyzers.Tests;

/// <summary>TNY2001: the tenant resolved from the request with nothing validating the caller's access to it.</summary>
public sealed class ResolutionTests
{
    private const string Usings = """
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.AspNetCore.Http;
        using Microsoft.Extensions.DependencyInjection;
        using Tenantry;
        using Tenantry.AspNetCore;

        """;

    [Fact]
    public Task ResolvingFromTheRequest_WithoutAValidator_IsReported() =>
        Verify.AnalyzerAsync<UnvalidatedResolutionAnalyzer>(Usings + """
            public static class Startup
            {
                public static void Register(IServiceCollection services) =>
                    services.AddTenantry<string>(tenant =>
                    {
                        {|TNY2001:tenant.ResolveFromHeader("X-Tenant-Id")|};
                        {|TNY2001:tenant.ResolveFromQueryString("tenant")|};
                        tenant.ResolveFromClaim("tenant_id");
                        tenant.UseInMemoryStore([]);
                    });
            }
            """);

    [Fact]
    public Task AValidatorInTheSameAddTenantry_ClearsIt() =>
        Verify.AnalyzerAsync<UnvalidatedResolutionAnalyzer>(Usings + """
            public static class Startup
            {
                public static void Register(IServiceCollection services) =>
                    services.AddTenantry<string>(tenant => tenant
                        .ResolveFromHeader("X-Tenant-Id")
                        .ValidateTenantAccessByClaim("tenant_id")
                        .UseInMemoryStore([]));
            }
            """);

    [Fact]
    public Task AValidatorTypeAnywhere_ClearsIt() =>
        Verify.AnalyzerAsync<UnvalidatedResolutionAnalyzer>(Usings + """
            public static class Startup
            {
                public static void Register(IServiceCollection services)
                {
                    services.AddTenantry<string>(tenant => tenant.ResolveFromHeader("X-Tenant-Id").UseInMemoryStore([]));
                    services.AddScoped<ITenantAccessValidator<string>, Membership>();
                }
            }

            public sealed class Membership : ITenantAccessValidator<string>
            {
                public ValueTask<bool> ValidateAsync(HttpContext context, ITenantDescriptor<string> tenant, CancellationToken cancellationToken) =>
                    ValueTask.FromResult(true);
            }
            """);

    [Fact]
    public Task ABuilderHandedToOtherCode_IsNotReported() =>
        Verify.AnalyzerAsync<UnvalidatedResolutionAnalyzer>(Usings + """
            public static class Startup
            {
                public static void Register(IServiceCollection services) =>
                    services.AddTenantry<string>(tenant =>
                    {
                        tenant.ResolveFromHeader("X-Tenant-Id").UseInMemoryStore([]);
                        tenant.AddOurAccessRules();
                    });

                private static void AddOurAccessRules(this ITenantBuilder<string> tenant)
                {
                }
            }
            """);
}
