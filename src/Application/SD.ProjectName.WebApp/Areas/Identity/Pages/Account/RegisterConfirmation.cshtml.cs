using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using SD.ProjectName.WebApp.Identity;

namespace SD.ProjectName.WebApp.Areas.Identity.Pages.Account
{
    public class RegisterConfirmationModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;

        public RegisterConfirmationModel(UserManager<ApplicationUser> userManager)
        {
            _userManager = userManager;
        }

        public string? Email { get; set; }

        public bool DisplayConfirmAccountLink { get; set; }

        public string? EmailConfirmationUrl { get; set; }

        public async Task<IActionResult> OnGetAsync(string email, string? returnUrl = null)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                return RedirectToPage("/Index");
            }

            var user = await _userManager.FindByEmailAsync(email);
            if (user == null)
            {
                return NotFound($"Unable to load user with email '{email}'.");
            }

            Email = email;
            var userId = await _userManager.GetUserIdAsync(user);

            var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
            EmailConfirmationUrl = Url.Page(
                "/Account/ConfirmEmail",
                pageHandler: null,
                values: new { area = "Identity", userId, code, returnUrl },
                protocol: Request.Scheme);

            // In production we rely on the email. We show the link in dev/test to make manual verification easier.
            DisplayConfirmAccountLink = true;

            return Page();
        }
    }
}
