using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using SD.ProjectName.Modules.Products.Application;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.Modules.Products.Infrastructure;

namespace SD.ProjectName.Tests.Products
{
    public class ProductCatalogExportServiceTests
    {
        [Fact]
        public async Task ProcessJob_ShouldExportSellerCatalogToCsv()
        {
            await using var context = CreateContext();
            context.Products.AddRange(
                new ProductModel { Title = "First", MerchantSku = "SKU-E1", Price = 10, Stock = 2, Category = "Cat", WorkflowState = ProductWorkflowStates.Active, SellerId = "seller-1" },
                new ProductModel { Title = "Draft Item", MerchantSku = "SKU-E2", Price = 5, Stock = 1, Category = "Cat", WorkflowState = ProductWorkflowStates.Draft, SellerId = "seller-1" },
                new ProductModel { Title = "Other Seller", MerchantSku = "SKU-E3", Price = 3, Stock = 1, Category = "Cat", WorkflowState = ProductWorkflowStates.Active, SellerId = "seller-2" });
            await context.SaveChangesAsync();

            var service = CreateService(context);
            var job = await service.QueueAsync("seller-1", new ProductExportOptions { Format = "csv", UseFilters = false });

            await service.ProcessJobAsync(job.Id);

            var storedJob = await context.ProductExportJobs.AsNoTracking().FirstAsync(j => j.Id == job.Id);
            Assert.Equal(ProductExportJobStatus.Completed, storedJob.Status);
            Assert.Equal(2, storedJob.TotalProducts);
            Assert.NotNull(storedJob.FileContent);

            var csv = Encoding.UTF8.GetString(storedJob.FileContent!);
            Assert.Contains("SKU-E1", csv);
            Assert.Contains("SKU-E2", csv);
            Assert.DoesNotContain("SKU-E3", csv);
            Assert.Contains("sku,title,description,price,stock,category", csv.Split('\n').First(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ProcessJob_ShouldHonorFilters()
        {
            await using var context = CreateContext();
            context.Products.AddRange(
                new ProductModel { Title = "Match Active", MerchantSku = "SKU-F1", Price = 10, Stock = 2, Category = "Cat", WorkflowState = ProductWorkflowStates.Active, SellerId = "seller-1" },
                new ProductModel { Title = "Draft Not Included", MerchantSku = "SKU-F2", Price = 5, Stock = 1, Category = "Cat", WorkflowState = ProductWorkflowStates.Draft, SellerId = "seller-1" });
            await context.SaveChangesAsync();

            var service = CreateService(context);
            var job = await service.QueueAsync("seller-1", new ProductExportOptions
            {
                Format = "csv",
                UseFilters = true,
                Search = "F1",
                WorkflowState = ProductWorkflowStates.Active
            });

            await service.ProcessJobAsync(job.Id);

            var storedJob = await context.ProductExportJobs.AsNoTracking().FirstAsync(j => j.Id == job.Id);
            var csv = Encoding.UTF8.GetString(storedJob.FileContent!);

            Assert.Equal(ProductExportJobStatus.Completed, storedJob.Status);
            Assert.Equal(1, storedJob.TotalProducts);
            Assert.Contains("SKU-F1", csv);
            Assert.DoesNotContain("SKU-F2", csv);
        }

        private static ProductCatalogExportService CreateService(ProductDbContext context)
        {
            var repository = new ProductRepository(context);
            var queue = new ProductExportQueue();
            var logger = Mock.Of<ILogger<ProductCatalogExportService>>();
            return new ProductCatalogExportService(context, repository, queue, logger);
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
