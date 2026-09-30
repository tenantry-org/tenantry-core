using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Tenantry;
using Tenantry.EfCore;

namespace Tenantry.Samples.SecureApi;

public sealed class Note : ITenantEntity<string>
{
    public int Id { get; set; }

    [MaxLength(64)]
    public string TenantId { get; set; } = string.Empty;

    [MaxLength(500)]
    public string Text { get; set; } = string.Empty;
}

public sealed class NotesDbContext(DbContextOptions<NotesDbContext> options) : MultiTenantDbContext<string>(options)
{
    public DbSet<Note> Notes => Set<Note>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Every query is filtered by TenantId, so lead indexes with it.
        modelBuilder.Entity<Note>().HasIndex(note => new { note.TenantId, note.Id });

        // Last, after your own configuration: applies the tenant query filters.
        base.OnModelCreating(modelBuilder);
    }
}

public sealed record CreateNote(string Text);

public sealed record NoteResponse(int Id, string Text);
