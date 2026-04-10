using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RRDA.Data
{
    public class RRDAContextFactory : IDesignTimeDbContextFactory<RRDADbContext>
    {
        public RRDADbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<RRDADbContext>();
            optionsBuilder.UseSqlServer("Server=.\\SQLEXPRESS;Database=RRDA.Db;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True;Integrated Security=True;Encrypt=True");
            return new RRDADbContext(optionsBuilder.Options);
        }
    }
}
