using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SD.ProjectName.Modules.Products.Infrastructure
{
    public class ProductDbContextFactory : IDesignTimeDbContextFactory<ProductDbContext>
    {
        private const string _connectionString = "Server=(localdb)\\mssqllocaldb;Database=SDProjectNameDB;Trusted_Connection=True;MultipleActiveResultSets=true";

        public ProductDbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<ProductDbContext>();

            optionsBuilder.UseSqlServer(_connectionString);

            return new ProductDbContext(optionsBuilder.Options);
        }
    }
}
