using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SD.ProjectName.WebApp.Data
{
    public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
    {
        private const string _connectionString = "Server=(localdb)\\mssqllocaldb;Database=SDProjectNameDB;Trusted_Connection=True;MultipleActiveResultSets=true";

        public ApplicationDbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
            optionsBuilder.UseSqlServer(_connectionString);

            return new ApplicationDbContext(optionsBuilder.Options);
        }
    }
}
