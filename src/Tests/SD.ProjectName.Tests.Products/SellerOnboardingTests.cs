using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Pages.Seller;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.Tests.Identity
{
    public class SellerOnboardingTests
    {
        [Fact]
        public async Task StoreStep_ShouldPersistProfileAndAdvance()
        {
            var user = new ApplicationUser
            {
                BusinessName = "Old name",
                OnboardingStatus = OnboardingStatuses.NotStarted,
                OnboardingStep = 0
            };
            var userManager = CreateUserManager(user);
            var payoutEncryption = new PayoutEncryptionService(DataProtectionProvider.Create("tests"));
            var sensitiveEncryption = new SensitiveDataEncryptionService(DataProtectionProvider.Create("tests"));
            var model = CreateModel(userManager.Object, payoutEncryption, sensitiveEncryption);
            model.StoreProfile = new OnboardingModel.StoreProfileInput
            {
                StoreName = "New Store",
                StoreDescription = "Great products"
            };

            var result = await model.OnPostStoreAsync();

            var redirect = Assert.IsType<RedirectToPageResult>(result);
            Assert.Equal(2, redirect.RouteValues?["step"]);
            Assert.Equal("New Store", user.BusinessName);
            Assert.Equal("Great products", user.StoreDescription);
            Assert.Equal(OnboardingStatuses.InProgress, user.OnboardingStatus);
            Assert.Equal(1, user.OnboardingStep);
            userManager.Verify(m => m.UpdateAsync(user), Times.Once);
        }

        [Fact]
        public async Task PayoutStep_ShouldMarkSubmissionPendingVerification()
        {
            var user = new ApplicationUser
            {
                BusinessName = "Store",
                OnboardingStatus = OnboardingStatuses.InProgress,
                OnboardingStep = 2
            };
            var userManager = CreateUserManager(user);
            var payoutEncryption = new PayoutEncryptionService(DataProtectionProvider.Create("tests"));
            var sensitiveEncryption = new SensitiveDataEncryptionService(DataProtectionProvider.Create("tests"));
            var model = CreateModel(userManager.Object, payoutEncryption, sensitiveEncryption);
            model.Payout = new PayoutPreferencesInput
            {
                PayoutMethod = "Paypal",
                PayoutAccount = "seller@pay.test"
            };

            var result = await model.OnPostPayoutAsync();

            var redirect = Assert.IsType<RedirectToPageResult>(result);
            Assert.Equal("Dashboard", redirect.PageName);
            Assert.Equal(OnboardingStatuses.PendingVerification, user.OnboardingStatus);
            Assert.Equal(3, user.OnboardingStep);
            Assert.Equal("Paypal", user.PayoutMethod);
            Assert.Equal("seller@pay.test", payoutEncryption.Reveal(user.PayoutAccount));
            Assert.NotNull(user.OnboardingCompletedOn);
            userManager.Verify(m => m.UpdateAsync(user), Times.Once);
            Assert.Equal("Store profile submitted and pending verification.", model.StatusMessage);
        }

        private static Mock<UserManager<ApplicationUser>> CreateUserManager(ApplicationUser user)
        {
            var store = new Mock<IUserStore<ApplicationUser>>();
            var userManager = new Mock<UserManager<ApplicationUser>>(
                store.Object,
                Options.Create(new IdentityOptions()),
                new PasswordHasher<ApplicationUser>(),
                Array.Empty<IUserValidator<ApplicationUser>>(),
                Array.Empty<IPasswordValidator<ApplicationUser>>(),
                new UpperInvariantLookupNormalizer(),
                new IdentityErrorDescriber(),
                Mock.Of<IServiceProvider>(),
                Mock.Of<ILogger<UserManager<ApplicationUser>>>());

            userManager.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(user);
            userManager.Setup(m => m.UpdateAsync(user)).ReturnsAsync(IdentityResult.Success);
            return userManager;
        }

        private static OnboardingModel CreateModel(UserManager<ApplicationUser> userManager, IPayoutEncryptionService payoutEncryption, ISensitiveDataEncryptionService sensitiveEncryption)
        {
            var httpContext = new DefaultHttpContext();
            var modelState = new ModelStateDictionary();
            var actionContext = new ActionContext(httpContext, new RouteData(), new PageActionDescriptor(), modelState);
            var pageContext = new PageContext(actionContext)
            {
                ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), modelState)
            };

            var model = new OnboardingModel(userManager, payoutEncryption, sensitiveEncryption)
            {
                PageContext = pageContext,
                TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>())
            };

            return model;
        }
    }
}
