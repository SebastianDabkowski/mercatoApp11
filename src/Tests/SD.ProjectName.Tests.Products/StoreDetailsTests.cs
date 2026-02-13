using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Moq;
using SD.ProjectName.Modules.Products.Application;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.Modules.Products.Domain.Interfaces;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Pages.Store;

namespace SD.ProjectName.Tests.Products
{
    public class StoreDetailsTests
    {
        [Fact]
        public async Task OnGet_ShouldReturnPublicStore_WhenActive()
        {
            var user = CreateSeller(AccountStatuses.Verified, OnboardingStatuses.Completed, "Active Store");
            var model = CreateModel(
                new[] { user },
                new List<ProductModel> { new() { Id = 1, Title = "Product", Description = "Desc", Price = 10, Category = "Cat", WorkflowState = ProductWorkflowStates.Active, SellerId = user.Id } });

            var result = await model.OnGetAsync("active-store");

            Assert.IsType<PageResult>(result);
            Assert.True(model.IsPubliclyVisible);
            Assert.NotNull(model.Store);
            Assert.Equal("Active Store", model.Store!.Name);
            Assert.Single(model.ProductPreview);
        }

        [Fact]
        public async Task OnGet_ShouldReturnUnavailableMessage_WhenNotVerified()
        {
            var user = CreateSeller(AccountStatuses.Unverified, OnboardingStatuses.InProgress, "Hidden Store");
            var model = CreateModel(new[] { user }, new List<ProductModel>());

            var result = await model.OnGetAsync("hidden-store");

            Assert.IsType<PageResult>(result);
            Assert.False(model.IsPubliclyVisible);
            Assert.Equal(StatusCodes.Status404NotFound, model.Response.StatusCode);
            Assert.False(string.IsNullOrEmpty(model.StatusMessage));
        }

        [Fact]
        public async Task OnGet_ShouldMatchSlugIgnoringCaseAndSpaces()
        {
            var user = CreateSeller(AccountStatuses.Verified, OnboardingStatuses.PendingVerification, "Fancy Store Front");
            var model = CreateModel(new[] { user }, new List<ProductModel>());

            var result = await model.OnGetAsync("fancy-store-front");

            Assert.IsType<PageResult>(result);
            Assert.True(model.IsPubliclyVisible);
            Assert.Equal("Fancy Store Front", model.Store!.Name);
        }

        private static DetailsModel CreateModel(IEnumerable<ApplicationUser> users, List<ProductModel> products)
        {
            var userManager = CreateUserManager(users);
            var getProducts = CreateGetProducts(products);

            var httpContext = new DefaultHttpContext();
            var modelState = new ModelStateDictionary();
            var actionContext = new ActionContext(httpContext, new RouteData(), new PageActionDescriptor(), modelState);
            var pageContext = new PageContext(actionContext)
            {
                ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), modelState)
            };

            var model = new DetailsModel(userManager.Object, getProducts)
            {
                PageContext = pageContext
            };

            return model;
        }

        private static Mock<UserManager<ApplicationUser>> CreateUserManager(IEnumerable<ApplicationUser> users)
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

            userManager.Setup(m => m.Users).Returns(users.AsQueryable());

            return userManager;
        }

        private static GetProducts CreateGetProducts(List<ProductModel> products)
        {
            var repo = new Mock<IProductRepository>();
            repo.Setup(r => r.GetList(It.IsAny<string?>(), false)).ReturnsAsync(products);
            return new GetProducts(repo.Object);
        }

        private static ApplicationUser CreateSeller(string accountStatus, string onboardingStatus, string businessName)
        {
            return new ApplicationUser
            {
                Id = Guid.NewGuid().ToString(),
                AccountType = AccountTypes.Seller,
                AccountStatus = accountStatus,
                OnboardingStatus = onboardingStatus,
                BusinessName = businessName,
                StoreDescription = "Description",
                ContactEmail = "store@example.com"
            };
        }
    }
}
