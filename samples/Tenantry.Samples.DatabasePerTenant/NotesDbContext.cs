using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Tenantry;

namespace Tenantry.Samples.DatabasePerTenant;

public sealed class Note : ITenantEntity<string>
{
    public int Id { get; set; }

    [MaxLength(64)]
    public string TenantId { get; set; } = string.Empty;

    [MaxLength(500)]
    public string Text { get; set; } = string.Empty;
}

// Pooled, so its only constructor takes the options.
public sealed class NotesDbContext(DbContextOptions<NotesDbContext> options) : DbContext(options)
{
    public DbSet<Note> Notes => Set<Note>();
}
