# ASP.NET Core Identity

ASP.NET Core Identity keeps users in your database. When tenants share that database, make the user type tenant-owned
and its user names unique within a tenant. Tenantry then keeps each tenant's users apart like any other tenant-owned
entity.

## The user type and context

```csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Tenantry;

public class AppUser : IdentityUser, ITenantEntity<Guid>
{
    public Guid TenantId { get; set; }
}

public class AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options)
    : IdentityDbContext<AppUser>(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Identity makes user names unique across the table. Make them unique within a tenant, under the same name.
        var users = builder.Entity<AppUser>();
        users.Metadata.RemoveIndex([users.Metadata.FindProperty(nameof(AppUser.NormalizedUserName))!]);
        users.HasIndex(u => new { u.NormalizedUserName, u.TenantId }).HasDatabaseName("UserNameIndex").IsUnique();
    }
}
```

Register the context with `UseTenantry()`, and Identity's stores as usual:

```csharp
builder.Services.AddDbContext<AppIdentityDbContext>(options => options
    .UseSqlServer(connectionString)
    .UseTenantry());

builder.Services.AddIdentity<AppUser, IdentityRole>()
    .AddEntityFrameworkStores<AppIdentityDbContext>();
```

Then:

- `UserManager` and `SignInManager` find only the current tenant's users. Two tenants can each have a user named
  alice, and a user signs in to the tenant the request resolves to.
- A new user gets the current tenant when it is saved. Creating one with no tenant current throws, as for any
  tenant-owned entity.
- Roles are shared by every tenant, and list only the current tenant's members. For roles of a tenant's own, make the
  role type tenant-owned too, and its names unique within a tenant (`RoleNameIndex`), the same way.
- Identity keys an external login by its provider and the provider's key, so a Google or Entra ID account can be
  linked to a user in one tenant only.

## Sign-in cookies

Every tenant's cookies are protected with the application's one key ring, so a user's cookie from one tenant also
decrypts for another ([Cookies](authentication-per-tenant.md#cookies)). Put the tenant in the cookie, and refuse a
signed-in user whose cookie names another tenant. The factory derives from the one that adds the user's roles, as
`AddIdentity<AppUser, IdentityRole>` does:

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

public sealed class TenantClaimsFactory(
    UserManager<AppUser> users, RoleManager<IdentityRole> roles, IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<AppUser, IdentityRole>(users, roles, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(AppUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim("tenant_id", user.TenantId.ToString()));
        return identity;
    }
}
```

```csharp
builder.Services.AddIdentity<AppUser, IdentityRole>()
    .AddEntityFrameworkStores<AppIdentityDbContext>()
    .AddClaimsPrincipalFactory<TenantClaimsFactory>();

builder.Services.AddTenantry<Guid>(tenant => tenant
    .ResolveFromSubdomain(o => o.BaseDomains.Add("example.com"))
    .UseStore<EfCoreTenantStore>()
    .ValidateTenantAccess((http, t) =>
        http.User.Identity?.IsAuthenticated != true ||
        http.User.FindFirst("tenant_id")?.Value == t.TenantId.ToString()));
```

The validator lets an anonymous caller through, so the sign-in page has its tenant current and finds the tenant's
users. Resolve the tenant from the request (its host or route), not from a claim, since nobody is signed in yet.

## Pipeline order

The cookie handler checks a signed-in user's security stamp against the store during authentication, every
`SecurityStampValidatorOptions.ValidationInterval` (30 minutes by default), so the tenant must be current before
authentication: with no tenant, the tenant-owned user is not found and the user is signed out. Resolve it with
`app.UseTenantResolution()`, and authorize after `app.UseTenantry()`, so no policy sees a tenant the validator has not
checked ([Authentication per tenant](authentication-per-tenant.md)):

```csharp
app.UseTenantResolution();
app.UseAuthentication();
app.UseTenantry();
app.UseAuthorization();
```

During authentication the tenant is current but not yet checked against the user. Cookie events such as
`OnValidatePrincipal`, `SecurityStampValidatorOptions.OnRefreshingPrincipal`, and claims transformations must not
grant claims, roles or permissions from the current tenant, and must not write as it. Do not renew or reissue the
cookie with claims taken from the tenant: a cookie valid on several tenants carries them across.

A user signed in to one tenant who visits another, with a cookie shared across subdomains, gets one of two outcomes:

- When the security stamp is checked on that request (every `ValidationInterval`), the tenant's users do not include
  them, so Identity rejects the cookie and deletes it: the request runs anonymous, and the user is signed out of
  their own tenant too.
- Otherwise the validator refuses the tenant for a signed-in user, so the whole request is refused (`403`), sign-in
  page and other `AllowMissingTenant()` pages included, and the response sets no cookie.

Either way the user must sign out before signing in to another tenant, or use a cookie per tenant host. Pages that
must work for them, such as a sign-in page or static files, go before `app.UseTenantResolution()`.

## See also

- [Authentication per tenant](authentication-per-tenant.md)
- [Access control](access-control.md)
- [EF Core integration](efcore-integration.md)
