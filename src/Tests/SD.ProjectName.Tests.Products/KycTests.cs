using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Pages.Seller;

namespace SD.ProjectName.Tests.Identity
{
    public class KycTests
    {
        [Fact]
        public void CompanyInput_ShouldRequireCompanyFields()
        {
            var input = new KycModel.InputModel
            {
                SellerType = SellerTypes.Company,
                ConfirmAccuracy = true
            };

            var results = ValidateModel(input);

            Assert.Contains(results, r => r.MemberNames.Contains(nameof(KycModel.InputModel.CompanyName)));
            Assert.Contains(results, r => r.MemberNames.Contains(nameof(KycModel.InputModel.RegistrationNumber)));
            Assert.Contains(results, r => r.MemberNames.Contains(nameof(KycModel.InputModel.TaxId)));
            Assert.Contains(results, r => r.MemberNames.Contains(nameof(KycModel.InputModel.RegisteredAddress)));
            Assert.Contains(results, r => r.MemberNames.Contains(nameof(KycModel.InputModel.ContactPerson)));
            Assert.Contains(results, r => r.MemberNames.Contains(nameof(KycModel.InputModel.ContactEmail)));
        }

        [Fact]
        public void IndividualInput_ShouldRequirePersonalFields()
        {
            var input = new KycModel.InputModel
            {
                SellerType = SellerTypes.Individual,
                ConfirmAccuracy = true
            };

            var results = ValidateModel(input);

            Assert.Contains(results, r => r.MemberNames.Contains(nameof(KycModel.InputModel.FullName)));
            Assert.Contains(results, r => r.MemberNames.Contains(nameof(KycModel.InputModel.PersonalIdNumber)));
            Assert.Contains(results, r => r.MemberNames.Contains(nameof(KycModel.InputModel.Address)));
            Assert.Contains(results, r => r.MemberNames.Contains(nameof(KycModel.InputModel.ContactEmail)));
        }

        [Fact]
        public async Task OnPost_ShouldPersistCompanyDataAndMarkPending()
        {
            var user = new ApplicationUser();
            var userManager = CreateUserManager(user);
            var model = CreateModel(userManager.Object);
            model.Input = new KycModel.InputModel
            {
                SellerType = SellerTypes.Company,
                CompanyName = "Acme Ltd",
                RegistrationNumber = "REG-123",
                TaxId = "TAX-987",
                RegisteredAddress = "123 Market Street",
                ContactPerson = "Jane Reviewer",
                ContactEmail = "contact@acme.test",
                ContactPhone = "123-456-7890",
                ConfirmAccuracy = true
            };

            var result = await model.OnPostAsync();

            Assert.IsType<PageResult>(result);
            Assert.Equal(KycStatuses.Pending, user.KycStatus);
            Assert.NotNull(user.KycSubmittedOn);
            Assert.Equal(SellerTypes.Company, user.SellerType);
            Assert.Equal("Acme Ltd", user.BusinessName);
            Assert.Equal("REG-123", user.CompanyRegistrationNumber);
            Assert.Equal("TAX-987", user.TaxId);
            Assert.Equal("123 Market Street", user.Address);
            Assert.Equal("Jane Reviewer", user.VerificationContactName);
            Assert.Equal("contact@acme.test", user.ContactEmail);
            Assert.Equal("123-456-7890", user.ContactPhone);
            userManager.Verify(m => m.UpdateAsync(user), Times.Once);
            Assert.Contains("pending review", model.StatusMessage, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task PendingStatus_ShouldBlockResubmission()
        {
            var user = new ApplicationUser
            {
                KycStatus = KycStatuses.Pending,
                KycSubmittedOn = DateTimeOffset.UtcNow
            };
            var userManager = CreateUserManager(user);
            var model = CreateModel(userManager.Object);
            model.Input = new KycModel.InputModel
            {
                SellerType = SellerTypes.Individual,
                FullName = "Test Person",
                PersonalIdNumber = "PID-789",
                Address = "789 Review Lane",
                ContactEmail = "person@test.com",
                ConfirmAccuracy = true
            };

            var result = await model.OnPostAsync();

            Assert.IsType<PageResult>(result);
            Assert.Contains(model.ModelState[string.Empty]?.Errors ?? [], e => e.ErrorMessage.Contains("pending", StringComparison.OrdinalIgnoreCase));
            userManager.Verify(m => m.UpdateAsync(It.IsAny<ApplicationUser>()), Times.Never);
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

        private static KycModel CreateModel(UserManager<ApplicationUser> userManager)
        {
            var httpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "user") }, "Test"))
            };
            var modelState = new ModelStateDictionary();
            var actionContext = new ActionContext(httpContext, new RouteData(), new PageActionDescriptor(), modelState);
            var pageContext = new PageContext(actionContext)
            {
                ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), modelState)
            };

            var model = new KycModel(userManager, Options.Create(new KycOptions()))
            {
                PageContext = pageContext,
                TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>())
            };

            return model;
        }

        private static IList<ValidationResult> ValidateModel(object model)
        {
            var results = new List<ValidationResult>();
            var context = new ValidationContext(model, null, null);
            Validator.TryValidateObject(model, context, results, validateAllProperties: true);

            if (model is IValidatableObject validatable)
            {
                results.AddRange(validatable.Validate(context));
            }

            return results;
        }
    }
}
