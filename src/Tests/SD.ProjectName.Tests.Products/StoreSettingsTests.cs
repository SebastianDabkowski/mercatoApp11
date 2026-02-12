using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging;
using Moq;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Pages.Seller;

namespace SD.ProjectName.Tests.Products
{
    public class StoreSettingsTests
    {
        [Fact]
        public async Task Update_ShouldPersistProfileAndLogo()
        {
            var user = new ApplicationUser
            {
                Id = "user1",
                BusinessName = "Old",
                StoreDescription = "Old desc",
                ContactEmail = "old@example.com"
            };
            var userManager = CreateUserManager(user);
            userManager.Setup(m => m.Users).Returns(new[] { user }.AsQueryable());
            var env = CreateEnvironment();
            var model = CreateModel(userManager.Object, env.Object);
            model.Input = new StoreSettingsModel.StoreProfileInput
            {
                StoreName = "New Store",
                StoreDescription = "New description",
                ContactEmail = "new@example.com",
                ContactPhone = "123-456",
                ContactWebsite = "https://example.com",
                LogoFile = CreateFormFile("logo.png", "image/png")
            };

            var result = await model.OnPostAsync();

            var redirect = Assert.IsType<RedirectToPageResult>(result);
            Assert.Equal("New Store", user.BusinessName);
            Assert.Equal("New description", user.StoreDescription);
            Assert.Equal("new@example.com", user.ContactEmail);
            Assert.Equal("123-456", user.ContactPhone);
            Assert.Equal("https://example.com", user.ContactWebsite);
            Assert.False(string.IsNullOrEmpty(user.StoreLogoPath));
            Assert.True(System.IO.File.Exists(Path.Combine(env.Object.WebRootPath!, "uploads", "store-logos", "user1_logo.png")));
            userManager.Verify(m => m.UpdateAsync(user), Times.Once);
        }

        [Fact]
        public async Task Update_ShouldRejectDuplicateStoreName()
        {
            var currentUser = new ApplicationUser { Id = "u1", BusinessName = "Mine", ContactEmail = "mine@example.com" };
            var existingUser = new ApplicationUser { Id = "u2", BusinessName = "Taken" };
            var userManager = CreateUserManager(currentUser);
            userManager.Setup(m => m.Users).Returns(new[] { currentUser, existingUser }.AsQueryable());
            var env = CreateEnvironment();
            var model = CreateModel(userManager.Object, env.Object);
            model.Input = new StoreSettingsModel.StoreProfileInput
            {
                StoreName = "Taken",
                StoreDescription = "Desc",
                ContactEmail = "mine@example.com"
            };

            var result = await model.OnPostAsync();

            Assert.IsType<PageResult>(result);
            Assert.True(model.ModelState.ContainsKey("Input.StoreName"));
            userManager.Verify(m => m.UpdateAsync(It.IsAny<ApplicationUser>()), Times.Never);
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

            userManager.Setup(m => m.GetUserAsync(It.IsAny<System.Security.Claims.ClaimsPrincipal>())).ReturnsAsync(user);
            userManager.Setup(m => m.UpdateAsync(user)).ReturnsAsync(IdentityResult.Success);
            return userManager;
        }

        private static Mock<IWebHostEnvironment> CreateEnvironment()
        {
            var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(root);
            var env = new Mock<IWebHostEnvironment>();
            env.Setup(e => e.WebRootPath).Returns(root);
            env.Setup(e => e.ContentRootPath).Returns(root);
            return env;
        }

        private static StoreSettingsModel CreateModel(UserManager<ApplicationUser> userManager, IWebHostEnvironment environment)
        {
            var httpContext = new DefaultHttpContext();
            var modelState = new ModelStateDictionary();
            var actionContext = new ActionContext(httpContext, new Microsoft.AspNetCore.Routing.RouteData(), new PageActionDescriptor(), modelState);
            var pageContext = new PageContext(actionContext)
            {
                ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), modelState)
            };

            var model = new StoreSettingsModel(userManager, environment, Mock.Of<ILogger<StoreSettingsModel>>())
            {
                PageContext = pageContext,
                TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>())
            };

            return model;
        }

        private static IFormFile CreateFormFile(string fileName, string contentType)
        {
            var content = new byte[] { 1, 2, 3, 4 };
            var stream = new MemoryStream(content);
            return new FormFile(stream, 0, content.Length, "file", fileName)
            {
                Headers = new HeaderDictionary(),
                ContentType = contentType
            };
        }
    }
}
