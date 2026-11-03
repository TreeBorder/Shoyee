using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Shoyee.Storage;

namespace Shoyee.Storage.Sqlite;

/// <summary>供 <c>dotnet ef</c> 使用的设计时工厂（迁移生成用，不参与运行）。</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ShoyeeDbContext>
{
    /// <inheritdoc />
    public ShoyeeDbContext CreateDbContext(string[] args)
    {
        var path = Environment.GetEnvironmentVariable("SHOYEE_DB_PATH")
                   ?? Path.Combine(Path.GetTempPath(), "shoyee-design.db");
        var options = new DbContextOptionsBuilder<ShoyeeDbContext>()
            .UseSqlite($"Data Source={path}", b => b.MigrationsAssembly(typeof(DesignTimeDbContextFactory).Assembly.GetName().Name))
            .Options;
        return new ShoyeeDbContext(options);
    }
}
