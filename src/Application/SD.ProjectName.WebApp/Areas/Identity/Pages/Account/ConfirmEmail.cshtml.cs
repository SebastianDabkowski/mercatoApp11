using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using SD.ProjectName.WebApp.Identity;

namespace SD.ProjectName.WebApp.Areas.Identity.Pages.Account
{
    public class ConfirmEmailModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;

        public ConfirmEmailModel(UserManager<ApplicationUser> userManager)
        {
            _userManager = userManager;
        }

        [TempData]
        public string StatusMessage { get; set; } = string.Empty;

        public bool Success { get; private set; }

        public string? ResendConfirmationUrl { get; private set; }

        public async Task<IActionResult> OnGetAsync(string? userId, string? code, string? returnUrl = null)
        {
            if (userId == null || code == null)
            {
                return RedirectToPage("/Index");
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return NotFound($"Unable to load user with ID '{userId}'.");
            }

            code = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
            var result = await _userManager.ConfirmEmailAsync(user, code);

            if (result.Succeeded)
            {
                if (user.AccountStatus == AccountStatuses.Unverified)
                {
                    user.AccountStatus = AccountStatuses.Verified;
                }

                user.EmailVerifiedOn ??= DateTimeOffset.UtcNow;
                await _userManager.UpdateAsync(user);
                Success = true;
            }

            StatusMessage = result.Succeeded
                ? "Thank you for confirming your email. Your account is now verified."
                : "Your verification link is invalid or has expired. Request a new verification email to finish setting up your account.";

            if (!result.Succeeded)
            {
                ResendConfirmationUrl = Url.Page(
                    "/Account/ResendEmailConfirmation",
                    pageHandler: null,
                    values: new { area = "Identity", email = user.Email, returnUrl },
                    protocol: Request.Scheme);
            }

            if (Success && !string.IsNullOrEmpty(returnUrl))
            {
                return LocalRedirect(returnUrl);
            }

            return Page();
        }
    }
}
