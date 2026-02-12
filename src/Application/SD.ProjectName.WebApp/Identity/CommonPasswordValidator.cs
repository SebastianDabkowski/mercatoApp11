using Microsoft.AspNetCore.Identity;

namespace SD.ProjectName.WebApp.Identity
{
    public class CommonPasswordValidator : IPasswordValidator<ApplicationUser>
    {
        private static readonly HashSet<string> CommonPasswords = new(StringComparer.OrdinalIgnoreCase)
        {
            "password",
            "password1",
            "password123",
            "12345678",
            "123456789",
            "qwerty123",
            "letmein!",
            "welcome1"
        };

        public Task<IdentityResult> ValidateAsync(UserManager<ApplicationUser> manager, ApplicationUser user, string? password)
        {
            if (string.IsNullOrWhiteSpace(password))
            {
                return Task.FromResult(IdentityResult.Failed(new IdentityError
                {
                    Description = "Password cannot be empty."
                }));
            }

            if (CommonPasswords.Contains(password))
            {
                return Task.FromResult(IdentityResult.Failed(new IdentityError
                {
                    Description = "Password is too common. Choose something more unique."
                }));
            }

            return Task.FromResult(IdentityResult.Success);
        }
    }
}
