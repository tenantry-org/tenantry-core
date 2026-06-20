// ReSharper disable UnusedParameter.Local
using Tenantry.AspNetCore.Extensions;
using Tenantry.Core;

var builder = WebApplication.CreateBuilder(args);

// Register Tenantry
builder.Services.AddTenantry<string>(tenant =>
{
    // Read the tenant ID from the X-Tenant-Id HTTP header.
    tenant.ResolveFromHeader("X-Tenant-Id");

    // We can chain multiple resolution methods together, they will be tried in order of registration.
    // Startup will fail without at least one registered resolver
    tenant.ResolveFromQueryString("tenant");
    
    // Prevent requests without a tenant ID from executing by default
    // Can be overridden with [AllowMissingTenant] (MVC) or .AllowMissingTenant() (Minimal APIs)
    // Omitting this will allow all requests to execute regardless of whether a tenant was resolved.
    // You could then explicitly require a tenant by using [RequireTenant] (MVC) or .RequireTenant() (Minimal APIs)
    tenant.RequireTenantByDefault();

    // We can register custom validators to put additional constraints on access
    // They have access to the entire HTTP context and the tenant info
    tenant.ValidateTenantAccess((ctx, tenantInfo) => 
        !ctx.Request.Headers.ContainsKey("X-Block-Access"));
    
    // Validators can be chained together and will be evaluated with logical AND (both this and the last must pass)
    // They can also be asynchronous
    tenant.ValidateTenantAccess(async (ctx, tenantInfo, ct) => 
        await ValueTask.FromResult(!ctx.Request.Headers.ContainsKey("X-Also-Block-Access")));
    
    // This extension allows a logical OR (but in addition to any other validators in the chain)
    // Just one of these needs to pass, but all previous validators must pass also, due to default logical AND
    tenant.ValidateTenantAccessAny(
        group => group.ValidateTenantAccess((ctx, _) => ctx.Request.Headers.ContainsKey("X-Tenant-Id")),
        group => group.ValidateTenantAccess((ctx, _) => ctx.Request.Headers.ContainsKey("X-Or-This-Alternative")));

    // In-memory store — replace with a database/cache-backed ITenantStore implementation in production.
    // Startup will fail without a registered store
    tenant.UseInMemoryStore(
    [
        new TenantDescriptor<string> { TenantId = "acme",   Name = "Acme Corp"  },
        new TenantDescriptor<string> { TenantId = "globex", Name = "Globex LLC" },
    ]);
});

var app = builder.Build();

// Add the tenant resolution middleware
// Must come before any endpoint that needs the tenant context.
// Place after UseAuthentication() if using claim-based resolution.
app.UseTenantry();

var orders = new List<Order>
{
    new() {TenantId = "acme", Description = "Order 1"},
    new() {TenantId = "acme", Description = "Order 2"},
    new() {TenantId = "globex", Description = "Order 3"}
};

// List orders for the current tenant only, do not execute if tenant is unresolved
app.MapGet("/orders", (ITenantContext<string> ctx) => 
        Results.Ok(orders.Where(o => o.TenantId == ctx.CurrentTenantId)))
    .RequireTenant(); // <- Has no effect here because we're already using RequireTenantByDefault()

// Show the resolved tenant for the current request, allow to execute if tenant is unresolved
app.MapGet("/me", (ITenantContext<string> ctx) =>
    ctx.HasTenant
        ? Results.Ok(new { TenantId = ctx.CurrentTenantId, ctx.CurrentTenant!.Name })
        : Results.NotFound("No tenant resolved."))
    .AllowMissingTenant();

// A completely tenant-agnostic endpoint
app.MapGet("/health", () => Results.Ok("ok"))
    .AllowMissingTenant();

await app.RunAsync();

internal class Order
{
    public string TenantId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
