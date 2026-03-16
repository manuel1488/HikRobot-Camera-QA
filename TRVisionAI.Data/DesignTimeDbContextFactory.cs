using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TRVisionAI.Data;

/// <summary>
/// Usada por las herramientas de EF (dotnet ef migrations add …) en tiempo de diseño.
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
