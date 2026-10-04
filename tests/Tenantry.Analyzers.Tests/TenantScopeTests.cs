using Tenantry.EfCore.Analyzers;

namespace Tenantry.Analyzers.Tests;

/// <summary>TNY3001, a tenant made current from a descriptor built in place, and TNY3002, blocking on RunInScopeAsync.</summary>
public sealed class TenantScopeTests
{
    private const string Usings = """
        using System.Threading;
        using System.Threading.Tasks;
        using Tenantry;

        """;

    [Fact]
    public Task ADescriptorBuiltInTheCall_IsReported_AndOneFromAVariableIsNot() =>
        Verify.AnalyzerAsync<TenantScopeAnalyzer>(Usings + """
            public static class Work
            {
                public static void Run(ITenantContextSetter<string> context, ITenantScopeFactory<string> scopes, ITenantDescriptor<string> stored)
                {
                    using (context.MakeCurrent({|TNY3001:new TenantDescriptor<string> { TenantId = "acme", Name = "Acme" }|}))
                    {
                    }

                    using var scope = scopes.CreateScope({|TNY3001:new TenantDescriptor<string> { TenantId = "acme", Name = "Acme" }|});

                    using (context.MakeCurrent(stored))
                    {
                    }
                }
            }
            """);

    [Fact]
    public Task BlockingOnRunInScopeAsync_IsNoted_AndAwaitingItIsNot() =>
        Verify.AnalyzerAsync<TenantScopeAnalyzer>(Usings + """
            public static class Work
            {
                public static void Block(ITenantScopeFactory<string> scopes)
                {
                    {|TNY3002:scopes.RunInScopeAsync("acme", (_, _) => Task.CompletedTask).Wait()|};
                    {|TNY3002:scopes.RunInScopeAsync("acme", (_, _) => Task.CompletedTask).GetAwaiter().GetResult()|};
                    {|TNY3002:scopes.RunInScopeAsync("acme", (_, _) => Task.CompletedTask).ConfigureAwait(false).GetAwaiter().GetResult()|};
                    _ = {|TNY3002:scopes.RunInScopeAsync("acme", (_, _) => Task.FromResult(1)).Result|};
                }

                public static async Task AwaitAsync(ITenantScopeFactory<string> scopes, CancellationToken ct)
                {
                    await scopes.RunInScopeAsync("acme", (_, _) => Task.CompletedTask, ct);
                    var task = Task.Delay(1, ct);
                    task.Wait(ct);
                }
            }
            """);
}
