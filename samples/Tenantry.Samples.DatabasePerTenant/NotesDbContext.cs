using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Tenantry;
using Tenantry.EfCore;

namespace Tenantry.Samples.DatabasePerTenant;

public sealed class Note : ITenantEntity<string>
{
    public int Id { get; set; }

    [MaxLength(64)]
    public string TenantId { get; set; } = string.Empty;

    [MaxLength(500)]
    public string Text { get; set; } = string.Empty;
}

public sealed class NotesDbContext(DbContextOptions<NotesDbContext> options, ITenantContext<string> tenantContext)
    : MultiTenantDbContext<string>(options, tenantContext)
{
    public DbSet<Note> Notes => Set<Note>();
}
