using Microsoft.EntityFrameworkCore;
using RRDA.Data;

namespace RRDA.RepImp;

public sealed class SettingsDbContextFactory : IDbContextFactory<RRDADbContext>
{
    public RRDADbContext CreateDbContext()
    {
        var connectionString = Properties.Settings.Default.ConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString))
            return new RRDAContextFactory().CreateDbContext([]);

        var options = new DbContextOptionsBuilder<RRDADbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new RRDADbContext(options);
    }
}
