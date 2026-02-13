using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SD.ProjectName.Modules.Products.Application;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.Modules.Products.Infrastructure;
using SD.ProjectName.WebApp.Pages.Api;

namespace SD.ProjectName.Tests.Products
{
    public class SearchSuggestionsModelTests
    {
        [Fact]
        public async Task OnGet_ShouldReturnEmpty_WhenBelowMinLength()
        {
            await using var context = CreateContext();
            var model = CreateModel(context);

            var result = await model.OnGetAsync("a", CancellationToken.None);

            var json = Assert.IsType<JsonResult>(result);
            var payload = Assert.IsType<SearchSuggestionResponse>(json.Value);
            Assert.Empty(payload.Queries);
            Assert.Empty(payload.Categories);
            Assert.Empty(payload.Products);
        }

        [Fact]
        public async Task OnGet_ShouldReturnMatchingCategoriesProductsAndQueries()
        {
            await using var context = CreateContext();
            context.Categories.Add(new CategoryModel { Name = "Cameras", Slug = "cameras", FullPath = "Electronics / Cameras", SortOrder = 0, IsActive = true });
            context.Categories.Add(new CategoryModel { Name = "Inactive", Slug = "inactive", FullPath = "Electronics / Inactive", SortOrder = 1, IsActive = false });
            context.Products.AddRange(
                new ProductModel { Title = "Red Camera", Description = "Mirrorless", MerchantSku = "SUG-1", Price = 899, Stock = 2, Category = "Electronics / Cameras", WorkflowState = ProductWorkflowStates.Active, ModerationStatus = ProductModerationStatuses.Approved, SellerId = "seller-1" },
                new ProductModel { Title = "Tripod", Description = "Red compact tripod", MerchantSku = "SUG-2", Price = 49, Stock = 3, Category = "Accessories", WorkflowState = ProductWorkflowStates.Active, ModerationStatus = ProductModerationStatuses.Approved, SellerId = "seller-1" });
            await context.SaveChangesAsync();

            var model = CreateModel(context);

            var result = await model.OnGetAsync("cam", CancellationToken.None);

            var json = Assert.IsType<JsonResult>(result);
            var payload = Assert.IsType<SearchSuggestionResponse>(json.Value);

            Assert.Contains(payload.Categories, c => c.Name.Contains("Cameras", StringComparison.OrdinalIgnoreCase));
            Assert.True(payload.Products.Any());
            Assert.Contains(payload.Queries, q => q.Contains("cam", StringComparison.OrdinalIgnoreCase));
            Assert.All(payload.Categories, c => Assert.False(string.IsNullOrWhiteSpace(c.Slug)));
        }

        private static SearchSuggestionsModel CreateModel(ProductDbContext context)
        {
            var repository = new ProductRepository(context);
            var getProducts = new GetProducts(repository);
            var manageCategories = new ManageCategories(context);
            return new SearchSuggestionsModel(getProducts, manageCategories);
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
