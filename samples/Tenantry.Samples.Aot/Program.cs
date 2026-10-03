using Tenantry;
using Tenantry.Samples.Aot;
using Tenantry.Samples.Aot.Models;
using Tenantry.Samples.Aot.Serialization;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});

// Rejected requests get problem details (application/problem+json) instead of an empty body.
builder.Services.AddProblemDetails();

// Every Tenantry builder method used here is trimming- and Native AOT-safe.
builder.Services.AddTenantry<string>(tenant => tenant
    .ResolveFromHeader("X-Tenant-Id")
    .ResolveFromSubdomain(options => options.BaseDomains.Add("localhost")) // acme.localhost:5268
    .ResolveFromPropagationHeader()                                      // the tenant another service sent
    .UseInMemoryStore(
    [
        new TenantDescriptor<string> { TenantId = "acme", Name = "Acme Corp" },
        new TenantDescriptor<string> { TenantId = "globex", Name = "Globex LLC" },
    ])
    .UseConnectionStrings(options => options.GetConnectionString = t => $"Database=orders_{t.TenantId}")
    .ConfigureResolution(options => options.TenantNotFoundStatusCode = StatusCodes.Status403Forbidden) // hide which tenants exist
    .AddHttpPropagation()                                                // clients with UseTenantry() send the tenant
    .UseResolver<TenantCookieResolver>());                               // a custom resolver, created by DI

// A client for another service: its requests carry the current tenant, which that service reads with
// ResolveFromPropagationHeader(). This sample calls itself, at the address it listens on.
builder.Services.AddHttpClient("self", client => client.BaseAddress = new Uri(builder.Configuration["SelfUrl"] ?? "http://localhost:5268"))
    .UseTenantry();

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

// /me again, through the HTTP client: the request it sends carries this request's tenant.
app.MapGet("/me/via-http", async (IHttpClientFactory clients, CancellationToken ct) =>
        await clients.CreateClient("self").GetStringAsync("/me", ct))
    .RequireTenant();

// The database a database-per-tenant application would connect this request to.
app.MapGet("/database", (CurrentTenantConnectionString<string> connectionString) => connectionString.Get())
    .RequireTenant();

app.MapGet("/health", () => "ok")
    .AllowMissingTenant();

await app.RunAsync();
