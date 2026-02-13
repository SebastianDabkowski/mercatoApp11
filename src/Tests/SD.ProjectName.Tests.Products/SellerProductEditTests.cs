using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using SD.ProjectName.Modules.Products.Application;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.Modules.Products.Domain.Interfaces;
using SD.ProjectName.Modules.Products.Infrastructure;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Pages.Seller.Products;

namespace SD.ProjectName.Tests.Products
{
    public class SellerProductEditTests
    {
        [Fact]
        public async Task OnGet_ShouldForbid_WhenProductNotOwnedBySeller()
        {
            var repository = new Mock<IProductRepository>();
            repository.Setup(r => r.GetById(2, true)).ReturnsAsync(new ProductModel
            {
                Id = 2,
                Title = "Other Seller Product",
                MerchantSku = "SKU-OTHER",
                Price = 10,
                Stock = 1,
                Category = "Cat",
                WorkflowState = ProductWorkflowStates.Active,
                SellerId = "different-seller"
            });

            var getProducts = new GetProducts(repository.Object);
            var updateProduct = new UpdateProduct(Mock.Of<IProductRepository>());
            var user = new ApplicationUser { Id = "current-seller", UserName = "seller@example.com" };
            var userManager = CreateUserManager(user);
            await using var dbContext = CreateContext();
            var categories = new ManageCategories(dbContext);
            var model = CreateModel(getProducts, updateProduct, userManager.Object, categories, repository.Object);

            var result = await model.OnGetAsync(2);

            Assert.IsType<ForbidResult>(result);
        }

        [Fact]
        public async Task OnPost_ShouldUpdateAllAttributes_ForOwnedProduct()
        {
            await using var dbContext = CreateContext();
            var categories = new ManageCategories(dbContext);
            var createdCategory = (await categories.CreateAsync("Electronics", null)).Category!;

            var product = new ProductModel
            {
                Id = 3,
                Title = "Original",
                MerchantSku = "SKU-ORIGINAL",
                Price = 10,
                Stock = 1,
                Category = "Cat",
                CategoryId = createdCategory.Id,
                WorkflowState = ProductWorkflowStates.Active,
                SellerId = "current-seller"
            };

            ProductModel? saved = null;
            var repository = new Mock<IProductRepository>();
            repository.Setup(r => r.GetById(product.Id, true)).ReturnsAsync(product);
            repository.Setup(r => r.Update(It.IsAny<ProductModel>())).Callback<ProductModel>(p => saved = p).Returns(Task.CompletedTask);

            var getProducts = new GetProducts(repository.Object);
            var updateProduct = new UpdateProduct(repository.Object);
            var user = new ApplicationUser { Id = "current-seller", UserName = "seller@example.com" };
            var userManager = CreateUserManager(user);
            var model = CreateModel(getProducts, updateProduct, userManager.Object, categories, repository.Object);
            model.Input = new EditModel.InputModel
            {
                Id = product.Id,
                Title = "Updated title",
                MerchantSku = "SKU-UPDATED",
                Price = 25,
                Stock = 5,
                CategoryId = createdCategory.Id,
                Description = "Updated description",
                MainImageUrl = "https://cdn.example.com/main.jpg",
                GalleryImageUrls = "https://cdn.example.com/img1.jpg, https://cdn.example.com/img2.jpg",
                WeightKg = 1.25m,
                LengthCm = 30m,
                WidthCm = 20m,
                HeightCm = 10m,
                ShippingMethods = "Courier, Locker"
            };

            var result = await model.OnPostAsync();

            Assert.IsType<RedirectToPageResult>(result);
            Assert.NotNull(saved);
            Assert.Equal("Updated title", saved!.Title);
            Assert.Equal("Updated description", saved.Description);
            Assert.Equal("https://cdn.example.com/main.jpg", saved.MainImageUrl);
            Assert.Equal("https://cdn.example.com/img1.jpg, https://cdn.example.com/img2.jpg", saved.GalleryImageUrls);
            Assert.Equal(1.25m, saved.WeightKg);
            Assert.Equal(30m, saved.LengthCm);
            Assert.Equal(20m, saved.WidthCm);
            Assert.Equal(10m, saved.HeightCm);
            Assert.Equal("Courier, Locker", saved.ShippingMethods);
            Assert.Equal(createdCategory.Id, saved.CategoryId);
            Assert.Equal(createdCategory.FullPath, saved.Category);
        }

        private static EditModel CreateModel(GetProducts getProducts, UpdateProduct updateProduct, UserManager<ApplicationUser> userManager, ManageCategories categories, IProductRepository repository)
        {
            var logger = Mock.Of<ILogger<EditModel>>();
            var httpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "current-seller") }))
            };
            var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
            var pageContext = new PageContext(actionContext);

            return new EditModel(getProducts, updateProduct, userManager, categories, logger, repository)
            {
                PageContext = pageContext,
                TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>())
            };
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
