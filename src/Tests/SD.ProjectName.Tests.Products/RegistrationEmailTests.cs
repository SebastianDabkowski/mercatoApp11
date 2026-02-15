using System.Linq;
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

namespace SD.ProjectName.Tests.Identity
{
    public class RegistrationEmailTests
    {
        [Fact]
        public async Task BuyerRegistration_ShouldSendConfirmationEmail()
        {
            var store = new Mock<IUserEmailStore<ApplicationUser>>();
            store.Setup(s => s.SetUserNameAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            store.Setup(s => s.SetEmailAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            var userManager = CreateUserManager(store);
            userManager.SetupGet(m => m.SupportsUserEmail).Returns(true);
            userManager.Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);
            userManager.Setup(m => m.AddToRoleAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);
            userManager.Setup(m => m.GenerateEmailConfirmationTokenAsync(It.IsAny<ApplicationUser>())).ReturnsAsync("token");
            userManager.Setup(m => m.GetUserIdAsync(It.IsAny<ApplicationUser>())).ReturnsAsync("user-id");

            var signInManager = CreateSignInManager(userManager.Object);
            signInManager.Setup(s => s.GetExternalAuthenticationSchemesAsync()).ReturnsAsync(Enumerable.Empty<AuthenticationScheme>());

            var emailSender = new Mock<IEmailSender>();
            var legalDocs = new Mock<ILegalDocumentService>();
            legalDocs.Setup(d => d.GetActiveVersionAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new LegalDocumentVersion
                {
                    Id = 42,
                    DocumentType = LegalDocumentTypes.TermsOfService,
                    VersionTag = "v1",
                    Content = "Terms",
                    EffectiveFrom = DateTimeOffset.UtcNow.AddDays(-1)
                });

            var consentService = new Mock<IConsentService>();
            consentService.Setup(c => c.GetActiveConsentsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ConsentDefinitionView>
                {
                    new(1, ConsentTypes.Newsletter, "Newsletter", "News updates", false, false, new ConsentVersionView(10, "v1", DateTimeOffset.UtcNow.AddDays(-2), "content"), null)
                });
            consentService.Setup(c => c.RecordUserConsentsAsync(It.IsAny<string>(), It.IsAny<IDictionary<string, bool>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ConsentOperationResult.Succeeded());

            var model = CreateRegisterModel(userManager, store, signInManager, emailSender, legalDocs, consentService);
            model.Input = new RegisterModel.InputModel
            {
                Email = "buyer@example.com",
                Password = "StrongPass123!",
                ConfirmPassword = "StrongPass123!",
                AccountType = AccountTypes.Buyer,
                FullName = "Buyer Example",
                Address = "456 Buyer Rd",
                Country = "USA",
                AcceptTerms = true
            };

            var result = await model.OnPostAsync();

            Assert.IsType<RedirectToPageResult>(result);
            emailSender.Verify(e => e.SendEmailAsync(
                "buyer@example.com",
                "Confirm your email",
                It.Is<string>(body => body.Contains("Welcome to Mercato", StringComparison.OrdinalIgnoreCase)
                    && body.Contains("no-reply@mercato.test", StringComparison.OrdinalIgnoreCase))),
                Times.Once);
        }

        private static RegisterModel CreateRegisterModel(
            Mock<UserManager<ApplicationUser>> userManager,
            Mock<IUserEmailStore<ApplicationUser>> store,
            Mock<SignInManager<ApplicationUser>> signInManager,
            Mock<IEmailSender> emailSender,
            Mock<ILegalDocumentService> legalDocs,
            Mock<IConsentService> consents)
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Scheme = "https";
            var modelState = new ModelStateDictionary();
            var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor(), modelState);
            var pageContext = new PageContext(actionContext)
            {
                ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), modelState)
            };

            var model = new RegisterModel(
                userManager.Object,
                store.Object,
                signInManager.Object,
                Mock.Of<ILogger<RegisterModel>>(),
                emailSender.Object,
                Options.Create(new KycOptions()),
                Options.Create(new EmailOptions()),
                legalDocs.Object,
                consents.Object)
            {
                PageContext = pageContext,
                Url = new TestUrlHelper(actionContext),
                TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>())
            };

            return model;
        }

        private static Mock<UserManager<ApplicationUser>> CreateUserManager(Mock<IUserEmailStore<ApplicationUser>> store)
        {
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
            {
                CallBase = false
            };
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
                return "https://example.com/confirm";
            }
        }
    }
}
