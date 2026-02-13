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
                WorkflowState = ProductWorkflowStates.Active, ModerationStatus = ProductModerationStatuses.Approved,
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
                WorkflowState = ProductWorkflowStates.Active, ModerationStatus = ProductModerationStatuses.Approved,
                SellerId = "seller-1"
            });
            await context.SaveChangesAsync();

            var result = await service.DeleteAsync(electronics.Id);

            Assert.False(result.Success);
            Assert.NotNull(await context.Categories.FindAsync(electronics.Id));
        }

        [Fact]
        public async Task Rename_ShouldMoveCategoryToNewParent_AndRefreshPaths()
        {
            await using var context = CreateContext();
            var service = new ManageCategories(context);
            var electronics = (await service.CreateAsync("Electronics", null)).Category!;
            var accessories = (await service.CreateAsync("Accessories", electronics.Id)).Category!;
            var gadgets = (await service.CreateAsync("Gadgets", accessories.Id)).Category!;
            var clearance = (await service.CreateAsync("Clearance", null)).Category!;

            context.Products.Add(new ProductModel
            {
                Title = "Widget",
                MerchantSku = "SKU-3",
                Price = 20,
                Stock = 4,
                CategoryId = gadgets.Id,
                Category = "Electronics / Accessories / Gadgets",
                WorkflowState = ProductWorkflowStates.Active, ModerationStatus = ProductModerationStatuses.Approved,
                SellerId = "seller-1"
            });
            await context.SaveChangesAsync();

            var result = await service.RenameAsync(accessories.Id, "Accessories", null, clearance.Id);

            var refreshedGadgets = await context.Categories.FirstAsync(c => c.Id == gadgets.Id);
            var product = await context.Products.FirstAsync();
            Assert.True(result.Success);
            Assert.Equal(clearance.Id, (await context.Categories.FirstAsync(c => c.Id == accessories.Id)).ParentId);
            Assert.Equal("Clearance / Accessories / Gadgets", refreshedGadgets.FullPath);
            Assert.Equal("Clearance / Accessories / Gadgets", product.Category);
        }

        [Fact]
        public async Task Delete_ShouldReassignProducts_WhenTargetProvided()
        {
            await using var context = CreateContext();
            var service = new ManageCategories(context);
            var electronics = (await service.CreateAsync("Electronics", null)).Category!;
            var phones = (await service.CreateAsync("Phones", electronics.Id)).Category!;
            var home = (await service.CreateAsync("Home", null)).Category!;

            context.Products.Add(new ProductModel
            {
                Title = "Phone",
                MerchantSku = "SKU-4",
                Price = 400,
                Stock = 7,
                CategoryId = phones.Id,
                Category = "Electronics / Phones",
                WorkflowState = ProductWorkflowStates.Active, ModerationStatus = ProductModerationStatuses.Approved,
                SellerId = "seller-1"
            });
            await context.SaveChangesAsync();

            var result = await service.DeleteAsync(phones.Id, home.Id);

            var product = await context.Products.FirstAsync();
            Assert.True(result.Success);
            Assert.Null(await context.Categories.FindAsync(phones.Id));
            Assert.Equal(home.Id, product.CategoryId);
            Assert.Equal(home.FullPath, product.Category);
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
