using LogAnalyzerForWindows.Models;
using Microsoft.EntityFrameworkCore;

namespace LogAnalyzerForWindows.Database;

internal sealed class LogAnalyzerDbContext : DbContext
{
    private readonly string _dbPath;

    public DbSet<LogEntryEntity> LogEntries { get; set; } = null!;

    public LogAnalyzerDbContext()
    {
        var folder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var appFolder = Path.Combine(folder, "LogAnalyzerForWindows");
        Directory.CreateDirectory(appFolder);
        _dbPath = Path.Combine(appFolder, "logs.db");
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite($"Data Source={_dbPath}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LogEntryEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Timestamp).IsRequired(false);
            entity.Property(e => e.Level).HasMaxLength(50);
            entity.Property(e => e.Message).HasMaxLength(4000);
            entity.Property(e => e.SessionId).HasMaxLength(100);
            entity.Property(e => e.CreatedAt).IsRequired();

            entity.HasIndex(e => e.Level);
            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => e.SessionId);
            entity.HasIndex(e => e.CreatedAt);
        });
    }
}
