using Microsoft.EntityFrameworkCore;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.Modules.Products.Infrastructure;

namespace SD.ProjectName.Tests.Products
{
    public class ProductRepositoryTests
    {
        [Fact]
        public async Task GetList_ShouldExcludeArchivedProducts()
        {
            await using var context = CreateContext();
            context.Products.AddRange(
                new ProductModel { Title = "Active", MerchantSku = "SKU-A1", Price = 10, Stock = 1, Category = "Cat", WorkflowState = ProductWorkflowStates.Active, SellerId = "seller-1" },
                new ProductModel { Title = "Draft", MerchantSku = "SKU-A2", Price = 5, Stock = 2, Category = "Cat", WorkflowState = ProductWorkflowStates.Draft, SellerId = "seller-1" },
                new ProductModel { Title = "Archived", MerchantSku = "SKU-A3", Price = 3, Stock = 0, Category = "Cat", WorkflowState = ProductWorkflowStates.Archived, SellerId = "seller-1" });
            await context.SaveChangesAsync();

            var repository = new ProductRepository(context);

            var results = await repository.GetList("seller-1", includeDrafts: true);

            Assert.Equal(2, results.Count);
            Assert.DoesNotContain(results, p => p.WorkflowState == ProductWorkflowStates.Archived);
        }

        [Fact]
        public async Task GetList_ShouldHideSuspendedFromPublicListings()
        {
            await using var context = CreateContext();
            context.Products.AddRange(
                new ProductModel { Title = "Active", MerchantSku = "SKU-B1", Price = 10, Stock = 1, Category = "Cat", WorkflowState = ProductWorkflowStates.Active, SellerId = "seller-1" },
                new ProductModel { Title = "Suspended", MerchantSku = "SKU-B2", Price = 5, Stock = 2, Category = "Cat", WorkflowState = ProductWorkflowStates.Suspended, SellerId = "seller-1" });
            await context.SaveChangesAsync();

            var repository = new ProductRepository(context);

            var results = await repository.GetList();

            Assert.Single(results);
            Assert.DoesNotContain(results, p => p.WorkflowState == ProductWorkflowStates.Suspended);
        }

        [Fact]
        public async Task GetById_ShouldReturnNull_WhenArchived()
        {
            await using var context = CreateContext();
            var product = new ProductModel { Title = "Archived", MerchantSku = "SKU-C1", Price = 3, Stock = 0, Category = "Cat", WorkflowState = ProductWorkflowStates.Archived, SellerId = "seller-1" };
            context.Products.Add(product);
            await context.SaveChangesAsync();

            var repository = new ProductRepository(context);

            var result = await repository.GetById(product.Id, includeDrafts: true);

            Assert.Null(result);
        }

        [Fact]
        public async Task GetByIds_ShouldExcludeArchived()
        {
            await using var context = CreateContext();
            var active = new ProductModel { Title = "Active", MerchantSku = "SKU-D1", Price = 10, Stock = 1, Category = "Cat", WorkflowState = ProductWorkflowStates.Active, SellerId = "seller-1" };
            var draft = new ProductModel { Title = "Draft", MerchantSku = "SKU-D2", Price = 5, Stock = 2, Category = "Cat", WorkflowState = ProductWorkflowStates.Draft, SellerId = "seller-1" };
            var archived = new ProductModel { Title = "Archived", MerchantSku = "SKU-D3", Price = 3, Stock = 0, Category = "Cat", WorkflowState = ProductWorkflowStates.Archived, SellerId = "seller-1" };
            context.Products.AddRange(active, draft, archived);
            await context.SaveChangesAsync();

            var repository = new ProductRepository(context);

            var results = await repository.GetByIds(new[] { active.Id, draft.Id, archived.Id }, includeDrafts: true);

            Assert.Equal(2, results.Count);
            Assert.DoesNotContain(results, p => p.WorkflowState == ProductWorkflowStates.Archived);
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
