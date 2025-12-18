using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LogAnalyzerForWindows.Database;

internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<LogAnalyzerDbContext>
{
    public LogAnalyzerDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<LogAnalyzerDbContext>();
        optionsBuilder.UseSqlite(DbContextConfig.ConnectionString);

        return new LogAnalyzerDbContext(optionsBuilder.Options);
    }
}
