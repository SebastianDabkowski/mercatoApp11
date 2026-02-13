using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using SD.ProjectName.Modules.Products.Application;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.Modules.Products.Infrastructure;

namespace SD.ProjectName.Tests.Products
{
    public class ProductCatalogImportServiceTests
    {
        [Fact]
        public async Task Preview_ShouldRejectMissingHeaders()
        {
            await using var context = CreateContext();
            var service = CreateService(context);

            var bytes = Encoding.UTF8.GetBytes("Title,Price,Stock\nItem,10,5");

            var preview = await service.PreviewAsync("seller-1", bytes, "products.csv");

            Assert.Contains(preview.Errors, e => e.Message.Contains("Missing required column", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task Preview_ShouldCountCreatesAndUpdates()
        {
            await using var context = CreateContext();
            context.Products.Add(new ProductModel
            {
                Title = "Existing",
                MerchantSku = "SKU-1",
                Price = 5,
                Stock = 1,
                Category = "General",
                WorkflowState = ProductWorkflowStates.Active,
                SellerId = "seller-1"
            });
            await context.SaveChangesAsync();

            var service = CreateService(context);
            var csv = """
SKU,Title,Price,Stock,Category
SKU-1,Existing Updated,9.99,2,General
SKU-2,New Item,4.50,3,General
""";
            var preview = await service.PreviewAsync("seller-1", Encoding.UTF8.GetBytes(csv), "products.csv");

            Assert.Equal(2, preview.TotalRows);
            Assert.Equal(1, preview.UpdateCount);
            Assert.Equal(1, preview.CreateCount);
            Assert.Empty(preview.Errors);
        }

        [Fact]
        public async Task ProcessJob_ShouldCreateAndUpdateProducts()
        {
            await using var context = CreateContext();
            context.Products.Add(new ProductModel
            {
                Title = "Existing",
                MerchantSku = "SKU-1",
                Price = 5,
                Stock = 1,
                Category = "General",
                WorkflowState = ProductWorkflowStates.Active,
                SellerId = "seller-1"
            });
            await context.SaveChangesAsync();

            var service = CreateService(context);
            var csv = """
SKU,Title,Price,Stock,Category
SKU-1,Existing Updated,9.99,2,General
SKU-2,New Item,4.50,3,General
""";
            var (preview, job) = await service.CreatePendingJobAsync("seller-1", Encoding.UTF8.GetBytes(csv), "products.csv");
            Assert.NotNull(job);
            Assert.NotNull(preview);

            await service.ProcessJobAsync(job!.Id);

            var updatedJob = await context.ProductImportJobs.AsNoTracking().FirstAsync(j => j.Id == job.Id);
            Assert.Equal(ProductImportJobStatus.Completed, updatedJob.Status);
            Assert.Equal(1, updatedJob.UpdatedCount);
            Assert.Equal(1, updatedJob.CreatedCount);

            var products = await context.Products.AsNoTracking().OrderBy(p => p.MerchantSku).ToListAsync();
            Assert.Equal(2, products.Count);
            Assert.Equal("Existing Updated", products[0].Title);
            Assert.Equal("New Item", products[1].Title);
        }

        private static ProductCatalogImportService CreateService(ProductDbContext context)
        {
            var categories = new ManageCategories(context);
            var repo = new ProductRepository(context);
            var queue = new ProductImportQueue();
            var logger = Mock.Of<ILogger<ProductCatalogImportService>>();
            return new ProductCatalogImportService(context, categories, repo, queue, logger);
        }

        private static ProductDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<ProductDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            return new ProductDbContext(options);
        }
    }
}
