using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using SD.ProjectName.WebApp.Areas.Identity.Pages.Account;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.Tests.Identity
{
    public class ExternalLoginTests
    {
        [Fact]
        public async Task SocialLogin_ShouldCreateBuyerAndSignIn()
        {
            var userManager = CreateUserManager();
            var signInManager = CreateSignInManager(userManager.Object);
            var info = CreateExternalLoginInfo("google-key", "Google", "buyer@example.com", "Buyer Example");

            userManager.Setup(m => m.FindByLoginAsync("Google", "google-key")).ReturnsAsync((ApplicationUser?)null);
            userManager.Setup(m => m.FindByEmailAsync("buyer@example.com")).ReturnsAsync((ApplicationUser?)null);
            userManager.Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(IdentityResult.Success);
            userManager.Setup(m => m.AddToRoleAsync(It.IsAny<ApplicationUser>(), AccountTypes.Buyer)).ReturnsAsync(IdentityResult.Success);
            userManager.Setup(m => m.AddLoginAsync(It.IsAny<ApplicationUser>(), It.IsAny<UserLoginInfo>())).ReturnsAsync(IdentityResult.Success);
            userManager.Setup(m => m.UpdateAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(IdentityResult.Success);

            signInManager.Setup(s => s.GetExternalLoginInfoAsync(It.IsAny<string?>())).ReturnsAsync(info);
            signInManager.Setup(s => s.SignInAsync(It.IsAny<ApplicationUser>(), false, "Google")).Returns(Task.CompletedTask);
            signInManager.Setup(s => s.UpdateExternalAuthenticationTokensAsync(info)).ReturnsAsync(IdentityResult.Success);

            var model = CreateExternalLoginModel(userManager, signInManager);

            var result = await model.OnGetCallbackAsync();

            var redirect = Assert.IsType<LocalRedirectResult>(result);
            Assert.Equal("~/Buyer/Dashboard", redirect.Url);
            userManager.Verify(m => m.CreateAsync(It.Is<ApplicationUser>(u =>
                u.Email == "buyer@example.com" &&
                u.AccountType == AccountTypes.Buyer &&
                u.AccountStatus == AccountStatuses.Verified &&
                u.EmailConfirmed)), Times.Once);
            signInManager.Verify(s => s.SignInAsync(It.IsAny<ApplicationUser>(), false, "Google"), Times.Once);
        }

        [Fact]
        public async Task SocialLogin_ForSeller_ShouldBeRejected()
        {
            var seller = new ApplicationUser
            {
                Email = "seller@example.com",
                AccountType = AccountTypes.Seller
            };

            var userManager = CreateUserManager();
            var signInManager = CreateSignInManager(userManager.Object);
            var info = CreateExternalLoginInfo("facebook-key", "Facebook", seller.Email, "Seller Example");

            userManager.Setup(m => m.FindByLoginAsync("Facebook", "facebook-key")).ReturnsAsync((ApplicationUser?)null);
            userManager.Setup(m => m.FindByEmailAsync(seller.Email)).ReturnsAsync(seller);
            userManager.Setup(m => m.UpdateAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(IdentityResult.Success);

            signInManager.Setup(s => s.GetExternalLoginInfoAsync(It.IsAny<string?>())).ReturnsAsync(info);

            var model = CreateExternalLoginModel(userManager, signInManager);

            var result = await model.OnGetCallbackAsync("/return-path");

            var redirect = Assert.IsType<RedirectToPageResult>(result);
            Assert.Equal("./Login", redirect.PageName);
            Assert.Equal("Social login is currently available for buyers only.", model.ErrorMessage);
            signInManager.Verify(s => s.SignInAsync(It.IsAny<ApplicationUser>(), It.IsAny<bool>(), It.IsAny<string?>()), Times.Never);
            userManager.Verify(m => m.AddLoginAsync(It.IsAny<ApplicationUser>(), It.IsAny<UserLoginInfo>()), Times.Never);
        }

        private static ExternalLoginModel CreateExternalLoginModel(
            Mock<UserManager<ApplicationUser>> userManager,
            Mock<SignInManager<ApplicationUser>> signInManager,
            IUserCartService? userCartService = null)
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Scheme = "https";

            var modelState = new ModelStateDictionary();
            var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor(), modelState);
            var pageContext = new PageContext(actionContext)
            {
                ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), modelState)
            };

            var legalDocs = new Mock<ILegalDocumentService>();
            legalDocs.Setup(l => l.GetActiveVersionAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new LegalDocumentVersion
                {
                    Id = 7,
                    DocumentType = LegalDocumentTypes.TermsOfService,
                    VersionTag = "v1",
                    Content = "Terms",
                    EffectiveFrom = DateTimeOffset.UtcNow.AddDays(-1)
                });

            return new ExternalLoginModel(
                signInManager.Object,
                userManager.Object,
                Mock.Of<ILogger<ExternalLoginModel>>(),
                userCartService ?? Mock.Of<IUserCartService>(),
                legalDocs.Object)
            {
                PageContext = pageContext,
                Url = new TestUrlHelper(actionContext),
                TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>())
            };
        }

        private static Mock<UserManager<ApplicationUser>> CreateUserManager()
        {
            var store = new Mock<IUserStore<ApplicationUser>>();
            return new Mock<UserManager<ApplicationUser>>(
                store.Object,
                Options.Create(new IdentityOptions()),
                new PasswordHasher<ApplicationUser>(),
                Array.Empty<IUserValidator<ApplicationUser>>(),
                Array.Empty<IPasswordValidator<ApplicationUser>>(),
                new UpperInvariantLookupNormalizer(),
                new IdentityErrorDescriber(),
                Mock.Of<IServiceProvider>(),
                Mock.Of<ILogger<UserManager<ApplicationUser>>>())
            { CallBase = false };
        }

        private static Mock<SignInManager<ApplicationUser>> CreateSignInManager(UserManager<ApplicationUser> userManager)
        {
            var contextAccessor = new Mock<IHttpContextAccessor>();
            contextAccessor.SetupGet(c => c.HttpContext).Returns(new DefaultHttpContext());
            var claimsFactory = new Mock<IUserClaimsPrincipalFactory<ApplicationUser>>();
            var options = Options.Create(new IdentityOptions());
            var logger = Mock.Of<ILogger<SignInManager<ApplicationUser>>>();
            var schemes = new Mock<IAuthenticationSchemeProvider>();
            schemes.Setup(s => s.GetAllSchemesAsync()).ReturnsAsync(Array.Empty<AuthenticationScheme>());
            var confirmation = new Mock<IUserConfirmation<ApplicationUser>>();
            confirmation.Setup(c => c.IsConfirmedAsync(It.IsAny<UserManager<ApplicationUser>>(), It.IsAny<ApplicationUser>())).ReturnsAsync(true);

            var signInManager = new Mock<SignInManager<ApplicationUser>>(userManager, contextAccessor.Object, claimsFactory.Object, options, logger, schemes.Object, confirmation.Object)
            {
                CallBase = false
            };

            return signInManager;
        }

        private static ExternalLoginInfo CreateExternalLoginInfo(string providerKey, string provider, string email, string? name = null)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Email, email) };
            if (!string.IsNullOrEmpty(name))
            {
                claims.Add(new Claim(ClaimTypes.Name, name));
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, provider));
            return new ExternalLoginInfo(principal, provider, providerKey, provider);
        }

        private class TestUrlHelper : IUrlHelper
        {
            public ActionContext ActionContext { get; }

            public TestUrlHelper(ActionContext actionContext)
            {
                ActionContext = actionContext;
            }

            public string? Action(UrlActionContext actionContext) => "/";

            public string? Action(string? action, string? controller, object? values, string? protocol, string? host, string? fragment) => "/";

            public string Content(string? contentPath)
            {
                if (string.IsNullOrEmpty(contentPath))
                {
                    return "/";
                }

                return contentPath == "~/" ? "/" : contentPath;
            }

            public bool IsLocalUrl(string? url) => url != null && (url.StartsWith('/') || url.StartsWith("~/"));

            public string? Link(string? routeName, object? values) => "/";

            public string? RouteUrl(UrlRouteContext routeContext) => "/";

            public string? RouteUrl(string? routeName, object? values, string? protocol, string? host, string? fragment) => "/";

            public string? Page(string? pageName, string? pageHandler = null, object? values = null, string? protocol = null, string? host = null, string? fragment = null) => "/";
        }
    }
}
