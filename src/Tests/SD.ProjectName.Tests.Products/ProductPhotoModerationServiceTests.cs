using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.Modules.Products.Infrastructure;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.Tests.Products
{
    public class ProductPhotoModerationServiceTests
    {
        [Fact]
        public async Task FlagAsync_HidesFlaggedPhotoUntilReviewed()
        {
            await using var dbContext = CreateContext();
            var product = new ProductModel
            {
                Id = 10,
                Title = "Sample",
                MerchantSku = "SKU1",
                Price = 12,
                Stock = 5,
                Category = "Cat",
                WorkflowState = ProductWorkflowStates.Active,
                ModerationStatus = ProductModerationStatuses.Approved,
                SellerId = "seller-1",
                MainImageUrl = "https://cdn.example.com/main.jpg",
                GalleryImageUrls = "https://cdn.example.com/alt.jpg"
            };
            dbContext.Products.Add(product);
            await dbContext.SaveChangesAsync();

            var userManager = CreateUserManager(new ApplicationUser { Id = "seller-1", BusinessName = "Seller One", Email = "seller@test.com" });
            var service = new ProductPhotoModerationService(dbContext, userManager.Object, null);

            await service.SyncFromProductAsync(product);
            var result = await service.FlagAsync(product.Id, product.MainImageUrl!, "moderator", "Flagged", isMain: true);

            Assert.True(result.Success);
            var updated = await dbContext.Products.FirstAsync(p => p.Id == product.Id);
            Assert.Equal("https://cdn.example.com/alt.jpg", updated.MainImageUrl);
            Assert.Null(updated.GalleryImageUrls);

            var queue = await service.GetQueueAsync();
            Assert.Single(queue.Items);
            var queued = queue.Items.First();
            Assert.Equal(product.Id, queued.ProductId);
            Assert.Equal("Seller One", queued.SellerName);
        }

        [Fact]
        public async Task RemoveAsync_RemovesPhotoAndKeepsGalleryIntact()
        {
            await using var dbContext = CreateContext();
            var product = new ProductModel
            {
                Id = 20,
                Title = "Gallery Product",
                MerchantSku = "SKU2",
                Price = 20,
                Stock = 3,
                Category = "Cat",
                WorkflowState = ProductWorkflowStates.Active,
                ModerationStatus = ProductModerationStatuses.Approved,
                SellerId = "seller-2",
                MainImageUrl = "https://cdn.example.com/main.jpg",
                GalleryImageUrls = "https://cdn.example.com/first.jpg, https://cdn.example.com/second.jpg"
            };
            dbContext.Products.Add(product);
            await dbContext.SaveChangesAsync();

            var userManager = CreateUserManager(new ApplicationUser { Id = "seller-2", BusinessName = "Seller Two" });
            var service = new ProductPhotoModerationService(dbContext, userManager.Object, null);

            await service.SyncFromProductAsync(product);
            var photoToRemove = await dbContext.ProductPhotos.FirstAsync(p => p.Url == "https://cdn.example.com/first.jpg");
            var removeResult = await service.RemoveAsync(new[] { photoToRemove.Id }, "moderator", "Policy violation");

            Assert.True(removeResult.Success);
            var updated = await dbContext.Products.FirstAsync(p => p.Id == product.Id);
            Assert.Equal("https://cdn.example.com/main.jpg", updated.MainImageUrl);
            Assert.Equal("https://cdn.example.com/second.jpg", updated.GalleryImageUrls);

            var removedPhoto = await dbContext.ProductPhotos.FirstAsync(p => p.Id == photoToRemove.Id);
            Assert.Equal(ProductPhotoStatuses.Removed, removedPhoto.Status);
            Assert.Equal("Policy violation", removedPhoto.ModerationNote);
        }

        private static Mock<UserManager<ApplicationUser>> CreateUserManager(ApplicationUser user)
        {
            var store = new Mock<IUserStore<ApplicationUser>>();
            var userManager = new Mock<UserManager<ApplicationUser>>(
                store.Object,
                null!,
                new PasswordHasher<ApplicationUser>(),
                Array.Empty<IUserValidator<ApplicationUser>>(),
                Array.Empty<IPasswordValidator<ApplicationUser>>(),
                new UpperInvariantLookupNormalizer(),
                new IdentityErrorDescriber(),
                null!,
                Mock.Of<ILogger<UserManager<ApplicationUser>>>());

            userManager.SetupGet(m => m.Users).Returns(new List<ApplicationUser> { user }.AsQueryable());
            userManager.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(user);
            return userManager;
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
