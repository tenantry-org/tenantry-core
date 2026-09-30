using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Tenantry;

namespace Tenantry.Samples.SecureApi;

public sealed class Note : ITenantEntity<string>
{
    public int Id { get; set; }

    [MaxLength(64)]
    public string TenantId { get; set; } = string.Empty;

    [MaxLength(500)]
    public string Text { get; set; } = string.Empty;
}

// A plain DbContext: UseTenantry() in Program.cs isolates its tenant-owned entities.
public sealed class NotesDbContext(DbContextOptions<NotesDbContext> options) : DbContext(options)
{
    public DbSet<Note> Notes => Set<Note>();

    // Every query is filtered by TenantId, so lead indexes with it.
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Note>().HasIndex(note => new { note.TenantId, note.Id });
}

public sealed record CreateNote(string Text);

public sealed record NoteResponse(int Id, string Text);
