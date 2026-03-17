using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TRVisionAI.Data;

/// <summary>
/// Used by EF tooling (dotnet ef migrations add …) at design time.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={DbPathHelper.DatabasePath}")
            .Options;

        return new AppDbContext(opts);
    }
}
