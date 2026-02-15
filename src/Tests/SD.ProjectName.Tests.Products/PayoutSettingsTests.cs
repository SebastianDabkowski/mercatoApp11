using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using SD.ProjectName.WebApp.Data;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Pages.Seller;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.Tests.Products;

public class PayoutSettingsTests
{
    [Fact]
    public async Task Save_BankTransfer_ShouldPersistEncryptedDetails()
    {
        var user = new ApplicationUser();
        var userManager = CreateUserManager(user);
        var encryption = new PayoutEncryptionService(DataProtectionProvider.Create("tests"));
        var model = CreateModel(userManager.Object, encryption);
        model.Input = new PayoutPreferencesInput
        {
            PayoutMethod = PayoutMethods.BankTransfer,
            BankAccountNumber = "DE44500105175407324931",
            BankRoutingNumber = "50010517"
        };

        var result = await model.OnPostAsync();

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(PayoutMethods.BankTransfer, user.PayoutMethod);
        Assert.Equal("DE44500105175407324931", encryption.Reveal(user.PayoutBankAccount));
        Assert.Equal("50010517", encryption.Reveal(user.PayoutBankRouting));
        userManager.Verify(m => m.UpdateAsync(user), Times.Once);
    }

    [Fact]
    public async Task Save_InvalidBankTransfer_ShouldShowErrors()
    {
        var user = new ApplicationUser();
        var userManager = CreateUserManager(user);
        var encryption = new PayoutEncryptionService(DataProtectionProvider.Create("tests"));
        var model = CreateModel(userManager.Object, encryption);
        model.Input = new PayoutPreferencesInput
        {
            PayoutMethod = PayoutMethods.BankTransfer,
            BankAccountNumber = "123",
            BankRoutingNumber = ""
        };

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.True(model.ModelState.ContainsKey("Input.BankAccountNumber"));
        Assert.True(model.ModelState.ContainsKey("Input.BankRoutingNumber"));
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

    private static PayoutSettingsModel CreateModel(UserManager<ApplicationUser> userManager, IPayoutEncryptionService encryptionService)
    {
        var httpContext = new DefaultHttpContext();
        var modelState = new ModelStateDictionary();
        var actionContext = new ActionContext(httpContext, new Microsoft.AspNetCore.Routing.RouteData(), new PageActionDescriptor(), modelState);
        var pageContext = new PageContext(actionContext)
        {
            ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), modelState)
        };

        var auditContext = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"payout-audit-{Guid.NewGuid()}")
            .Options);
        var auditService = new CriticalActionAuditService(auditContext, TimeProvider.System);

        var model = new PayoutSettingsModel(userManager, encryptionService, auditService)
        {
            PageContext = pageContext,
            TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>())
        };

        return model;
    }
}
