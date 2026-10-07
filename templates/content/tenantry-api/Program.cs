// A multi-tenant API, from the tenantry-api template (https://tenantry.dev/docs/core/ai-agents has the rules to keep).
//
// 1. Callers authenticate with a JWT bearer token. Anonymous requests get 401.
// 2. The caller selects a tenant with the X-Tenant-Id header. Tenantry checks it against the token's
//    "tenant_id" claims, so a caller can only select tenants they belong to (403 otherwise).
// 3. Every endpoint requires a tenant unless it opts out (400 without one). EF Core reads and writes are
//    isolated to the selected tenant, and tenant-owned writes without a tenant are rejected.
//
// `dotnet run` starts it in Development on port 5160 (Properties/launchSettings.json).
// Get a token, then call the API:
//   curl -X POST localhost:5160/dev/token -H 'Content-Type: application/json' \
//        -d '{"subject":"alice","tenants":["acme"]}'
//   curl localhost:5160/notes -H 'Authorization: Bearer <token>' -H 'X-Tenant-Id: acme'
//
// Replace the development token endpoint with your identity provider, and the in-memory store with one backed by
// your database, before going to production.

using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Tenantry;
using TenantryApi;

var builder = WebApplication.CreateBuilder(args);

var auth = builder.Configuration.GetSection("Auth").Get<AuthSettings>() ?? new AuthSettings();

if (builder.Environment.IsDevelopment() && string.IsNullOrEmpty(auth.SigningKey))
{
    // No key configured: sign development tokens with a random key for this run, so the sample works
    // from a fresh clone without committing a secret. Configure Auth:SigningKey (or real issuer keys)
    // everywhere else.
    auth = new AuthSettings
    {
        Issuer = auth.Issuer,
        Audience = auth.Audience,
        SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
    };
}

// Read now, so a missing or short Auth:SigningKey stops the application starting rather than failing every request.
var signingKey = auth.GetSigningKey();
var tenants = builder.Configuration.GetSection("Tenants").Get<List<TenantDescriptor<string>>>() ?? [];

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Keep claim names as issued ("sub", "tenant_id") instead of mapping them to long URIs.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new()
        {
            ValidIssuer = auth.Issuer,
            ValidAudience = auth.Audience,
            IssuerSigningKey = signingKey,
        };
    });

// Every endpoint requires an authenticated caller unless it explicitly allows anonymous access.
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

builder.Services.AddTenantry<string>(tenant =>
{
    tenant.ResolveFromHeader("X-Tenant-Id");
    tenant.ValidateTenantAccessByClaim(AuthSettings.TenantClaim); // 403 unless the token lists the tenant
    tenant.RequireTenantByDefault();                               // 400 when no tenant is selected
    tenant.UseInMemoryStore(tenants);                              // use a database-backed store in production
});

builder.Services.AddDbContext<NotesDbContext>(options => options
    .UseSqlite(builder.Configuration.GetConnectionString("Notes"))
    .UseTenantry()); // filters queries and checks writes by the current tenant

var app = builder.Build();

// Sample only: create the schema on startup. Use EF Core migrations in production.
await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<NotesDbContext>().Database.EnsureCreatedAsync();
}

// Behind a proxy that terminates TLS, call app.UseForwardedHeaders() here first, with ForwardedHeadersOptions that
// trust only that proxy, so these see the original HTTPS scheme.
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAuthentication();
// Authorization before Tenantry, so an anonymous caller gets 401 rather than 403: no policy here needs the tenant.
// With UseTenantResolution() (authentication settings per tenant), authorization goes after UseTenantry() instead.
app.UseAuthorization();
app.UseTenantry();

app.MapGet("/health", () => Results.Ok("healthy"))
    .AllowAnonymous()
    .AllowMissingTenant();

if (app.Environment.IsDevelopment())
{
    // Stands in for an identity provider so the sample runs on its own. Never expose this in production.
    app.MapPost("/dev/token", (DevelopmentTokenRequest request) =>
            Results.Ok(new { token = auth.IssueDevelopmentToken(request.Subject, request.Tenants) }))
        .AllowAnonymous()
        .AllowMissingTenant();
}

var notes = app.MapGroup("/notes");

notes.MapGet("/", async (NotesDbContext db, CancellationToken ct) =>
    await db.Notes.OrderBy(note => note.Id).Select(note => new NoteResponse(note.Id, note.Text)).ToListAsync(ct));

notes.MapPost("/", async (CreateNote request, NotesDbContext db, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > 500)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [nameof(CreateNote.Text)] = ["Text is required and must be at most 500 characters."],
        });
    }

    Note note = new() { Text = request.Text };
    db.Notes.Add(note);
    await db.SaveChangesAsync(ct);

    return Results.Created($"/notes/{note.Id}", new NoteResponse(note.Id, note.Text));
});

notes.MapDelete("/{id:int}", async (int id, NotesDbContext db, CancellationToken ct) =>
    await db.Notes.Where(note => note.Id == id).ExecuteDeleteAsync(ct) == 1
        ? Results.NoContent()
        : Results.NotFound());

await app.RunAsync();
