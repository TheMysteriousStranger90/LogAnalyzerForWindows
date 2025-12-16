using LogAnalyzerForWindows.Models;
using Microsoft.EntityFrameworkCore;

namespace LogAnalyzerForWindows.Database;

internal sealed class LogAnalyzerDbContext : DbContext
{
    public DbSet<LogEntryEntity> LogEntries { get; set; } = null!;

    public LogAnalyzerDbContext()
    {
    }

    public LogAnalyzerDbContext(DbContextOptions<LogAnalyzerDbContext> options) : base(options)
    {
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseSqlite(DbContextConfig.ConnectionString);
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LogEntryEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Timestamp);
            entity.Property(e => e.Level).HasMaxLength(50);
            entity.Property(e => e.Message);
            entity.Property(e => e.EventId);
            entity.Property(e => e.Source).HasMaxLength(256);
            entity.Property(e => e.CreatedAt);
            entity.Property(e => e.SessionId).HasMaxLength(100);

            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => e.Level);
            entity.HasIndex(e => e.EventId);
            entity.HasIndex(e => e.Source);
            entity.HasIndex(e => e.SessionId);
        });
    }
}
