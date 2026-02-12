using System.ComponentModel.DataAnnotations;
using System.Linq;
using Moq;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SD.ProjectName.WebApp.Areas.Identity.Pages.Account;
using SD.ProjectName.WebApp.Identity;

namespace SD.ProjectName.Tests.Products
{
    public class RegistrationValidationTests
    {
        [Fact]
        public void Seller_ShouldRequireBusinessData()
        {
            var input = new RegisterModel.InputModel
            {
                Email = "seller@example.com",
                Password = "StrongPass123!",
                ConfirmPassword = "StrongPass123!",
                AccountType = AccountTypes.Seller,
                FullName = "Seller Example",
                Address = "123 Market St",
                Country = "USA",
                AcceptTerms = true
            };

            var results = ValidateModel(input);

            Assert.Contains(results, r => r.MemberNames.Contains(nameof(RegisterModel.InputModel.BusinessName)));
            Assert.Contains(results, r => r.MemberNames.Contains(nameof(RegisterModel.InputModel.TaxId)));
        }

        [Fact]
        public void Terms_ShouldBeRequired()
        {
            var input = new RegisterModel.InputModel
            {
                Email = "buyer@example.com",
                Password = "StrongPass123!",
                ConfirmPassword = "StrongPass123!",
                AccountType = AccountTypes.Buyer,
                FullName = "Buyer Example",
                Address = "456 Buyer Rd",
                Country = "USA",
                AcceptTerms = false
            };

            var results = ValidateModel(input);

            Assert.Contains(results, r => r.MemberNames.Contains(nameof(RegisterModel.InputModel.AcceptTerms)));
        }

        [Fact]
        public async Task CommonPasswordValidator_ShouldRejectCommonPassword()
        {
            var validator = new CommonPasswordValidator();
            var userManager = CreateUserManager();

            var result = await validator.ValidateAsync(userManager, new ApplicationUser(), "password123");

            Assert.False(result.Succeeded);
            Assert.Contains(result.Errors, e => e.Description.Contains("common", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task CommonPasswordValidator_ShouldAcceptStrongPassword()
        {
            var validator = new CommonPasswordValidator();
            var userManager = CreateUserManager();

            var result = await validator.ValidateAsync(userManager, new ApplicationUser(), "BetterPass123!");

            Assert.True(result.Succeeded);
        }

        private static IList<ValidationResult> ValidateModel(object model)
        {
            var validationResults = new List<ValidationResult>();
            var context = new ValidationContext(model, null, null);
            Validator.TryValidateObject(model, context, validationResults, validateAllProperties: true);

            if (model is IValidatableObject validatable)
            {
                validationResults.AddRange(validatable.Validate(context));
            }

            return validationResults;
        }

        private static UserManager<ApplicationUser> CreateUserManager()
        {
            var store = new Mock<IUserStore<ApplicationUser>>();
            return new UserManager<ApplicationUser>(
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
    }
}
