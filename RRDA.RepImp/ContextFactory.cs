using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using RRDA.Data;

namespace RRDA.RepImp
{
    public class ContextFactory : IDesignTimeDbContextFactory<RRDADbContext>
    {
        public RRDADbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<RRDADbContext>();
            optionsBuilder.UseSqlServer("Server=.\\SQLEXPRESS;Database=RRDA.Db;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True;Integrated Security=True;Encrypt=True");
            return new RRDADbContext(optionsBuilder.Options);
        }
    }
}
