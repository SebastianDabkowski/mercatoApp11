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

        [Fact]
        public async Task GetListFiltered_ShouldRespectSearchAndWorkflowState()
        {
            await using var context = CreateContext();
            context.Products.AddRange(
                new ProductModel { Title = "Match One", MerchantSku = "SKU-F1", Price = 10, Stock = 1, Category = "Cat", WorkflowState = ProductWorkflowStates.Active, SellerId = "seller-1" },
                new ProductModel { Title = "Match Two", MerchantSku = "SKU-F2", Price = 5, Stock = 2, Category = "Cat", WorkflowState = ProductWorkflowStates.Draft, SellerId = "seller-1" },
                new ProductModel { Title = "Other Seller", MerchantSku = "SKU-F3", Price = 3, Stock = 0, Category = "Cat", WorkflowState = ProductWorkflowStates.Active, SellerId = "seller-2" });
            await context.SaveChangesAsync();

            var repository = new ProductRepository(context);

            var results = await repository.GetListFiltered("seller-1", includeDrafts: true, search: "F1", workflowState: ProductWorkflowStates.Active);

            Assert.Single(results);
            Assert.Equal("SKU-F1", results[0].MerchantSku);
        }

        [Fact]
        public async Task GetByCategoryIds_ShouldReturnActiveProducts_InProvidedCategories()
        {
            await using var context = CreateContext();
            context.Products.AddRange(
                new ProductModel { Title = "In Root", MerchantSku = "SKU-G1", Price = 10, Stock = 3, Category = "Root", CategoryId = 1, WorkflowState = ProductWorkflowStates.Active, SellerId = "seller-1" },
                new ProductModel { Title = "In Child", MerchantSku = "SKU-G2", Price = 8, Stock = 2, Category = "Root / Child", CategoryId = 2, WorkflowState = ProductWorkflowStates.Active, SellerId = "seller-1" },
                new ProductModel { Title = "Draft Child", MerchantSku = "SKU-G3", Price = 6, Stock = 1, Category = "Root / Child", CategoryId = 2, WorkflowState = ProductWorkflowStates.Draft, SellerId = "seller-1" },
                new ProductModel { Title = "Archived", MerchantSku = "SKU-G4", Price = 4, Stock = 0, Category = "Root", CategoryId = 1, WorkflowState = ProductWorkflowStates.Archived, SellerId = "seller-1" });
            await context.SaveChangesAsync();

            var repository = new ProductRepository(context);

            var results = await repository.GetByCategoryIds(new[] { 1, 2 });

            Assert.Equal(2, results.Count);
            Assert.DoesNotContain(results, p => p.WorkflowState != ProductWorkflowStates.Active);
            Assert.All(results, p => Assert.Contains(p.CategoryId!.Value, new[] { 1, 2 }));
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
