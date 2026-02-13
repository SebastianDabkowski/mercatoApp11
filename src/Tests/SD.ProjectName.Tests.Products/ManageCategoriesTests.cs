using Microsoft.EntityFrameworkCore;
using SD.ProjectName.Modules.Products.Application;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.Modules.Products.Infrastructure;

namespace SD.ProjectName.Tests.Products
{
    public class ManageCategoriesTests
    {
        [Fact]
        public async Task Rename_ShouldUpdateProductLabels_ForDescendants()
        {
            await using var context = CreateContext();
            var service = new ManageCategories(context);
            var electronics = (await service.CreateAsync("Electronics", null)).Category!;
            var laptops = (await service.CreateAsync("Laptops", electronics.Id)).Category!;

            context.Products.Add(new ProductModel
            {
                Title = "Laptop",
                MerchantSku = "SKU-1",
                Price = 999,
                Stock = 5,
                CategoryId = laptops.Id,
                Category = "Electronics / Laptops",
                WorkflowState = ProductWorkflowStates.Active,
                SellerId = "seller-1"
            });
            await context.SaveChangesAsync();

            var result = await service.RenameAsync(electronics.Id, "Tech");

            var product = await context.Products.FirstAsync();
            Assert.True(result.Success);
            Assert.Equal("Tech / Laptops", product.Category);
        }

        [Fact]
        public async Task Delete_ShouldFail_WhenProductsAssigned()
        {
            await using var context = CreateContext();
            var service = new ManageCategories(context);
            var electronics = (await service.CreateAsync("Electronics", null)).Category!;

            context.Products.Add(new ProductModel
            {
                Title = "Phone",
                MerchantSku = "SKU-2",
                Price = 500,
                Stock = 3,
                CategoryId = electronics.Id,
                Category = "Electronics",
                WorkflowState = ProductWorkflowStates.Active,
                SellerId = "seller-1"
            });
            await context.SaveChangesAsync();

            var result = await service.DeleteAsync(electronics.Id);

            Assert.False(result.Success);
            Assert.NotNull(await context.Categories.FindAsync(electronics.Id));
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
