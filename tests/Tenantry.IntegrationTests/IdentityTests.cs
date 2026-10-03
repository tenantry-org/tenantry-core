using AwesomeAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tenantry.IntegrationTests.Providers;

namespace Tenantry.IntegrationTests;

/// <summary>
/// ASP.NET Core Identity in a database its tenants share, as docs/efcore-advanced.md sets it up: the user type is
/// tenant-owned and its user names are unique within a tenant, so each tenant's <see cref="UserManager{TUser}"/> sees its
/// own users only.
/// </summary>
public sealed class IdentityTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private static readonly TenantDescriptor<string> Acme = new() { TenantId = "acme", Name = "Acme" };
    private static readonly TenantDescriptor<string> Globex = new() { TenantId = "globex", Name = "Globex" };

    private ServiceProvider _services = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        var connectionString = fixture.WithDatabase($"identity_{Guid.NewGuid():N}");

        ServiceCollection services = new();
        services.AddLogging();
        services.AddTenantry<string>(tenant => tenant.UseInMemoryStore([Acme, Globex]));
        services.AddDbContext<IdentityContext>(options => options.UseSqlServer(connectionString).UseTenantry());
        services.AddIdentityCore<TenantUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<IdentityContext>();
        _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        await using var scope = _services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IdentityContext>().Database.EnsureCreatedAsync(Ct);
    }

    public ValueTask DisposeAsync() => _services.DisposeAsync();

    [Fact]
    public async Task TwoTenants_EachHaveAUserNamedAlice_AndEachSeesOnlyItsOwn()
    {
        var acmeAlice = await CreateAsync(Acme, "alice");
        var globexAlice = await CreateAsync(Globex, "alice");

        await AsAsync(Acme, async users =>
        {
            (await users.FindByNameAsync("alice"))!.Id.Should().Be(acmeAlice);
            (await users.FindByIdAsync(globexAlice)).Should().BeNull();
        });

        await AsAsync(Globex, async users => (await users.FindByNameAsync("alice"))!.TenantId.Should().Be("globex"));
    }

    [Fact]
    public async Task AUserName_IsUniqueWithinATenant()
    {
        await CreateAsync(Acme, "bob");

        await AsAsync(Acme, async users =>
        {
            var result = await users.CreateAsync(new TenantUser { UserName = "bob" }, Password);
            result.Errors.Select(e => e.Code).Should().Equal(nameof(IdentityErrorDescriber.DuplicateUserName));
        });
    }

    [Fact]
    public async Task ARoleTheTenantsShare_ListsTheCurrentTenantsMembersOnly()
    {
        await AsAsync(Acme, async (_, scope) =>
            (await scope.GetRequiredService<RoleManager<IdentityRole>>().CreateAsync(new IdentityRole("admin"))).Succeeded.Should().BeTrue());

        var acmeCarol = await CreateAsync(Acme, "carol", role: "admin");
        await CreateAsync(Globex, "carol", role: "admin");

        await AsAsync(Acme, async users =>
            (await users.GetUsersInRoleAsync("admin")).Select(u => u.Id).Should().Equal(acmeCarol));
    }

    [Fact]
    public async Task AnUpdate_KeepsTheUsersTenant()
    {
        var id = await CreateAsync(Acme, "dave");

        await AsAsync(Acme, async users =>
        {
            var dave = (await users.FindByIdAsync(id))!;
            dave.Email = "dave@acme.example";
            (await users.UpdateAsync(dave)).Succeeded.Should().BeTrue();
        });

        await AsAsync(Acme, async users => (await users.FindByEmailAsync("dave@acme.example"))!.Id.Should().Be(id));
        await AsAsync(Globex, async users => (await users.FindByEmailAsync("dave@acme.example")).Should().BeNull());
    }

    private const string Password = "Correct-horse-1";

    private async Task<string> CreateAsync(TenantDescriptor<string> tenant, string userName, string? role = null)
    {
        string? id = null;

        await AsAsync(tenant, async users =>
        {
            TenantUser user = new() { UserName = userName };
            (await users.CreateAsync(user, Password)).Succeeded.Should().BeTrue();
            user.TenantId.Should().Be(tenant.TenantId, "Tenantry stamps the new user with the current tenant");

            if (role is not null)
            {
                (await users.AddToRoleAsync(user, role)).Succeeded.Should().BeTrue();
            }

            id = user.Id;
        });

        return id!;
    }

    private Task AsAsync(TenantDescriptor<string> tenant, Func<UserManager<TenantUser>, Task> work) =>
        AsAsync(tenant, (users, _) => work(users));

    private async Task AsAsync(TenantDescriptor<string> tenant, Func<UserManager<TenantUser>, IServiceProvider, Task> work)
    {
        await using var scope = _services.GetRequiredService<ITenantScopeFactory<string>>().CreateScope(tenant);
        await work(scope.ServiceProvider.GetRequiredService<UserManager<TenantUser>>(), scope.ServiceProvider);
    }
}

public sealed class TenantUser : IdentityUser, ITenantEntity<string>
{
    public string TenantId { get; set; } = null!;
}

public sealed class IdentityContext(DbContextOptions<IdentityContext> options) : IdentityDbContext<TenantUser>(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Identity makes user names unique across the table. Each tenant has its own users, so make them unique within
        // a tenant instead, under the index's own name.
        var users = builder.Entity<TenantUser>();
        users.Metadata.RemoveIndex([users.Metadata.FindProperty(nameof(TenantUser.NormalizedUserName))!]);
        users.HasIndex(u => new { u.NormalizedUserName, u.TenantId }).HasDatabaseName("UserNameIndex").IsUnique();
        users.Property(u => u.TenantId).HasMaxLength(64);
    }
}
