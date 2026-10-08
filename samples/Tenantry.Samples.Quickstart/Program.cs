// ReSharper disable UnusedParameter.Local
// Introductory sample: tenants come from a header and callers are not authenticated, so any caller can
// select any tenant. See Tenantry.Samples.SecureApi for a production-shaped setup.
using Tenantry;
using Tenantry.Samples.Quickstart.Models;

var builder = WebApplication.CreateBuilder(args);

// Register Tenantry
builder.Services.AddTenantry<string>(tenant =>
{
    // Read the tenant ID from the X-Tenant-Id HTTP header.
    tenant.ResolveFromHeader("X-Tenant-Id");

    // Resolvers are tried in the order they are added. app.UseTenantry() fails at startup without one.
    tenant.ResolveFromQueryString("tenant");

    // Every endpoint requires a tenant unless it has [AllowMissingTenant] (MVC) or .AllowMissingTenant()
    // (minimal APIs). Without this line, every endpoint runs whether or not a tenant was resolved, unless it has
    // [RequireTenant] (MVC) or .RequireTenant() (minimal APIs).
    tenant.RequireTenantByDefault();

    // Access validators read the HttpContext and the tenant, and can refuse the tenant: a request that needs it
    // then gets 403.
    tenant.ValidateTenantAccess((ctx, tenantInfo) =>
        !ctx.Request.Headers.ContainsKey("X-Block-Access"));

    // Every validator must pass: this one and the one above. A validator can be asynchronous.
    tenant.ValidateTenantAccess(async (ctx, tenantInfo, ct) =>
        await ValueTask.FromResult(!ctx.Request.Headers.ContainsKey("X-Also-Block-Access")));

    // In production, replace the in-memory store with an ITenantStore over your database.
    // app.UseTenantry() fails at startup without a store.
    tenant.UseInMemoryStore(
    [
        new TenantDescriptor<string> { TenantId = "acme",   Name = "Acme Corp"  },
        new TenantDescriptor<string> { TenantId = "globex", Name = "Globex LLC" },
    ]);
});

var app = builder.Build();

// Resolves the tenant for each request and runs the access validators. It goes after UseAuthentication() when the
// app has authentication, and before anything that needs the tenant.
app.UseTenantry();

var orders = new List<Order>
{
    new() {TenantId = "acme", Description = "Order 1"},
    new() {TenantId = "acme", Description = "Order 2"},
    new() {TenantId = "globex", Description = "Order 3"}
};

// The current tenant's orders. The endpoint requires a tenant.
app.MapGet("/orders", (ITenantContext<string> ctx) =>
        Results.Ok(orders.Where(o => o.TenantId == ctx.CurrentTenantId)))
    .RequireTenant(); // no effect here, as RequireTenantByDefault() already requires one

// The current request's tenant. The endpoint also runs without one, and answers 404.
app.MapGet("/me", (ITenantContext<string> ctx) =>
    ctx.HasTenant
        ? Results.Ok(new { TenantId = ctx.CurrentTenantId, ctx.RequiredTenant.Name })
        : Results.NotFound("No tenant resolved."))
    .AllowMissingTenant();

// An endpoint that needs no tenant
app.MapGet("/health", () => Results.Ok("ok"))
    .AllowMissingTenant();

await app.RunAsync();
