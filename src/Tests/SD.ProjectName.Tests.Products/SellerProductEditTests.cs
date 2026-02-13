using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Moq;
using SD.ProjectName.Modules.Products.Application;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.Modules.Products.Domain.Interfaces;
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
            var model = CreateModel(getProducts, updateProduct, userManager.Object);

            var result = await model.OnGetAsync(2);

            Assert.IsType<ForbidResult>(result);
        }

        private static EditModel CreateModel(GetProducts getProducts, UpdateProduct updateProduct, UserManager<ApplicationUser> userManager)
        {
            var logger = Mock.Of<ILogger<EditModel>>();
            var httpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "current-seller") }))
            };
            var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
            var pageContext = new PageContext(actionContext);

            return new EditModel(getProducts, updateProduct, userManager, logger)
            {
                PageContext = pageContext
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
    }
}
