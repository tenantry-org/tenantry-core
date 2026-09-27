using Microsoft.EntityFrameworkCore;
using Tenantry.Core;
using Tenantry.EfCore;

namespace Tenantry.Samples.DatabasePerTenant;

public sealed class Note : ITenantScoped<string>
{
    public int Id { get; set; }

    public string TenantId { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;
}

public sealed class NotesDbContext(DbContextOptions<NotesDbContext> options, ITenantContext<string> tenantContext)
    : MultiTenantDbContext<string>(options, tenantContext)
{
    public DbSet<Note> Notes => Set<Note>();
}
