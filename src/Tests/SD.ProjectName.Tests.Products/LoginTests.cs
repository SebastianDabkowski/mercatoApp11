using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
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
using IdentitySignInResult = Microsoft.AspNetCore.Identity.SignInResult;

namespace SD.ProjectName.Tests.Identity
{
    public class LoginTests
    {
        [Fact]
        public async Task VerifiedBuyer_ShouldRedirectToBuyerDashboard()
        {
            var userManager = CreateUserManager();
            var user = new ApplicationUser
            {
                Email = "buyer@example.com",
                UserName = "buyer@example.com",
                AccountType = AccountTypes.Buyer,
                AccountStatus = AccountStatuses.Verified
            };
            userManager.Setup(m => m.FindByEmailAsync(user.Email)).ReturnsAsync(user);
            userManager.Setup(m => m.IsEmailConfirmedAsync(user)).ReturnsAsync(true);

            var signInManager = CreateSignInManager(userManager.Object);
            signInManager.Setup(s => s.PasswordSignInAsync(user.UserName, "Pass123!abcd", true, true))
                .ReturnsAsync(IdentitySignInResult.Success);

            var model = CreateLoginModel(userManager, signInManager);
            model.Input = new LoginModel.InputModel { Email = user.Email, Password = "Pass123!abcd", RememberMe = true };

            var result = await model.OnPostAsync();

            var redirect = Assert.IsType<LocalRedirectResult>(result);
            Assert.Equal("~/Buyer/Dashboard", redirect.Url);
            signInManager.Verify(s => s.PasswordSignInAsync(user.UserName, "Pass123!abcd", true, true), Times.Once);
        }

        [Fact]
        public async Task VerifiedAdmin_ShouldRedirectToAdminDashboard()
        {
            var userManager = CreateUserManager();
            var user = new ApplicationUser
            {
                Email = "admin@example.com",
                UserName = "admin@example.com",
                AccountType = AccountTypes.Admin,
                AccountStatus = AccountStatuses.Verified
            };
            userManager.Setup(m => m.FindByEmailAsync(user.Email)).ReturnsAsync(user);
            userManager.Setup(m => m.IsEmailConfirmedAsync(user)).ReturnsAsync(true);

            var signInManager = CreateSignInManager(userManager.Object);
            signInManager.Setup(s => s.PasswordSignInAsync(user.UserName, "Pass123!abcd", true, true))
                .ReturnsAsync(IdentitySignInResult.Success);

            var model = CreateLoginModel(userManager, signInManager);
            model.Input = new LoginModel.InputModel { Email = user.Email, Password = "Pass123!abcd", RememberMe = true };

            var result = await model.OnPostAsync();

            var redirect = Assert.IsType<LocalRedirectResult>(result);
            Assert.Equal("~/Admin/Dashboard", redirect.Url);
        }

        [Fact]
        public async Task UnverifiedSeller_ShouldBlockAndSendVerificationLink()
        {
            var userManager = CreateUserManager();
            var user = new ApplicationUser
            {
                Email = "seller@example.com",
                UserName = "seller@example.com",
                AccountType = AccountTypes.Seller,
                AccountStatus = AccountStatuses.Unverified
            };
            userManager.Setup(m => m.FindByEmailAsync(user.Email)).ReturnsAsync(user);
            userManager.Setup(m => m.IsEmailConfirmedAsync(user)).ReturnsAsync(false);
            userManager.Setup(m => m.GetUserIdAsync(user)).ReturnsAsync("user-id");
            userManager.Setup(m => m.GenerateEmailConfirmationTokenAsync(user)).ReturnsAsync("token");

            var signInManager = CreateSignInManager(userManager.Object);

            var emailSender = new Mock<IEmailSender>();
            emailSender.Setup(e => e.SendEmailAsync(user.Email, It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);

            var model = CreateLoginModel(userManager, signInManager, emailSender);
            model.Input = new LoginModel.InputModel { Email = user.Email, Password = "Pass123!abcd", RememberMe = true };

            var result = await model.OnPostAsync();

            Assert.IsType<PageResult>(result);
            Assert.True(model.ShowVerificationReminder);
            Assert.False(string.IsNullOrEmpty(model.ResendConfirmationUrl));
            Assert.Contains(model.ModelState[string.Empty]?.Errors ?? [], e => e.ErrorMessage.Contains("verify", StringComparison.OrdinalIgnoreCase));
            signInManager.Verify(s => s.PasswordSignInAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never);
            emailSender.Verify(e => e.SendEmailAsync(user.Email, It.IsAny<string>(), It.Is<string>(body => body.Contains("confirm", StringComparison.OrdinalIgnoreCase))), Times.Once);
        }

        [Fact]
        public async Task InvalidPassword_ShouldReturnGenericError()
        {
            var userManager = CreateUserManager();
            var user = new ApplicationUser
            {
                Email = "buyer@example.com",
                UserName = "buyer@example.com",
                AccountType = AccountTypes.Buyer,
                AccountStatus = AccountStatuses.Verified
            };
            userManager.Setup(m => m.FindByEmailAsync(user.Email)).ReturnsAsync(user);
            userManager.Setup(m => m.IsEmailConfirmedAsync(user)).ReturnsAsync(true);

            var signInManager = CreateSignInManager(userManager.Object);
            signInManager.Setup(s => s.PasswordSignInAsync(user.UserName, "WrongPass123!", true, true))
                .ReturnsAsync(IdentitySignInResult.Failed);

            var model = CreateLoginModel(userManager, signInManager);
            model.Input = new LoginModel.InputModel { Email = user.Email, Password = "WrongPass123!", RememberMe = true };

            var result = await model.OnPostAsync();

            Assert.IsType<PageResult>(result);
            Assert.Contains(model.ModelState[string.Empty]?.Errors ?? [], e => e.ErrorMessage == "Invalid login attempt.");
        }

        [Fact]
        public async Task ProvidedReturnUrl_ShouldBeRespectedWhenLocal()
        {
            var userManager = CreateUserManager();
            var user = new ApplicationUser
            {
                Email = "seller@example.com",
                UserName = "seller@example.com",
                AccountType = AccountTypes.Seller,
                AccountStatus = AccountStatuses.Verified,
                KycStatus = KycStatuses.Approved
            };
            userManager.Setup(m => m.FindByEmailAsync(user.Email)).ReturnsAsync(user);
            userManager.Setup(m => m.IsEmailConfirmedAsync(user)).ReturnsAsync(true);

            var signInManager = CreateSignInManager(userManager.Object);
            signInManager.Setup(s => s.PasswordSignInAsync(user.UserName, "Pass123!abcd", true, true))
                .ReturnsAsync(IdentitySignInResult.Success);

            var model = CreateLoginModel(userManager, signInManager);
            model.Input = new LoginModel.InputModel { Email = user.Email, Password = "Pass123!abcd", RememberMe = true };

            var result = await model.OnPostAsync("/custom-path");

            var redirect = Assert.IsType<LocalRedirectResult>(result);
            Assert.Equal("/custom-path", redirect.Url);
        }

        [Fact]
        public async Task SellerWithoutKyc_ShouldBeSentToKycPage_WhenRequired()
        {
            var userManager = CreateUserManager();
            var user = new ApplicationUser
            {
                Email = "seller@example.com",
                UserName = "seller@example.com",
                AccountType = AccountTypes.Seller,
                AccountStatus = AccountStatuses.Verified,
                KycStatus = KycStatuses.Pending
            };
            userManager.Setup(m => m.FindByEmailAsync(user.Email)).ReturnsAsync(user);
            userManager.Setup(m => m.IsEmailConfirmedAsync(user)).ReturnsAsync(true);

            var signInManager = CreateSignInManager(userManager.Object);
            signInManager.Setup(s => s.PasswordSignInAsync(user.UserName, "Pass123!abcd", true, true))
                .ReturnsAsync(IdentitySignInResult.Success);

            var model = CreateLoginModel(userManager, signInManager, null, new KycOptions { RequireSellerKyc = true });
            model.Input = new LoginModel.InputModel { Email = user.Email, Password = "Pass123!abcd", RememberMe = true };

            var result = await model.OnPostAsync();

            var redirect = Assert.IsType<LocalRedirectResult>(result);
            Assert.Equal("~/Seller/Kyc", redirect.Url);
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
                Mock.Of<ILogger<UserManager<ApplicationUser>>>());
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
            signInManager.Setup(s => s.GetExternalAuthenticationSchemesAsync()).ReturnsAsync(Enumerable.Empty<AuthenticationScheme>());
            return signInManager;
        }

        private static LoginModel CreateLoginModel(
            Mock<UserManager<ApplicationUser>> userManager,
            Mock<SignInManager<ApplicationUser>> signInManager,
            Mock<IEmailSender>? emailSender = null,
            KycOptions? kycOptions = null,
            ILoginAuditService? loginAuditService = null)
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Scheme = "https";

            var modelState = new ModelStateDictionary();
            var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor(), modelState);
            var pageContext = new PageContext(actionContext)
            {
                ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), modelState)
            };

            var model = new LoginModel(signInManager.Object, userManager.Object, Mock.Of<ILogger<LoginModel>>(), (emailSender ?? new Mock<IEmailSender>()).Object, Options.Create(kycOptions ?? new KycOptions()), loginAuditService ?? Mock.Of<ILoginAuditService>())
            {
                PageContext = pageContext,
                Url = new TestUrlHelper(actionContext),
                TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>())
            };

            return model;
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

            public string Content(string? contentPath) => contentPath == "~/" ? "/" : contentPath ?? "/";

            public bool IsLocalUrl(string? url) => url != null && (url.StartsWith('/') || url.StartsWith("~/"));

            public string? Link(string? routeName, object? values) => "/";

            public string? RouteUrl(UrlRouteContext routeContext) => "/";

            public string? RouteUrl(string? routeName, object? values, string? protocol, string? host, string? fragment) => "/";

            public string? Page(string? pageName, string? pageHandler = null, object? values = null, string? protocol = null, string? host = null, string? fragment = null)
            {
                if (pageName != null && pageName.Contains("ResendEmailConfirmation", StringComparison.OrdinalIgnoreCase))
                {
                    return "/resend";
                }

                return "/confirm";
            }
        }
    }
}
