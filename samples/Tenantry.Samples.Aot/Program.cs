using System.Text.Json.Serialization;
using Tenantry.AspNetCore.Extensions;
using Tenantry.Core;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});

builder.Services.AddTenantry<string>(tenant =>
{
    tenant.ResolveFromHeader("X-Tenant-Id");
    tenant.UseInMemoryStore(
    [
        new TenantDescriptor<string> { TenantId = "acme", Name = "Acme Corp" },
        new TenantDescriptor<string> { TenantId = "globex", Name = "Globex LLC" },
    ]);
});

var app = builder.Build();

app.UseTenantry();

var orders = new List<Order>
{
    new("acme", "Widget order", 250.00m),
    new("acme", "Gadget order", 175.50m),
    new("globex", "Sprocket order", 999.99m),
};

app.MapGet("/orders", (ITenantContext<string> ctx) =>
        orders.Where(o => o.TenantId == ctx.CurrentTenantId))
    .RequireTenant();

app.MapGet("/me", (ITenantContext<string> ctx) =>
        ctx.HasTenant
            ? Results.Ok(new TenantResponse(ctx.CurrentTenantId!, ctx.CurrentTenant!.Name))
            : Results.NotFound())
    .AllowMissingTenant();

app.MapGet("/health", () => "ok")
    .AllowMissingTenant();

await app.RunAsync();

internal record Order(string TenantId, string Description, decimal Amount);

internal record TenantResponse(string TenantId, string Name);

[JsonSerializable(typeof(IEnumerable<Order>))]
[JsonSerializable(typeof(TenantResponse))]
[JsonSerializable(typeof(string))]
internal partial class AppJsonSerializerContext : JsonSerializerContext;
