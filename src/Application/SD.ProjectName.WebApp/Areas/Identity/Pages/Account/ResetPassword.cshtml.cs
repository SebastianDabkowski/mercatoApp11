using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using SD.ProjectName.WebApp.Identity;

namespace SD.ProjectName.WebApp.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class ResetPasswordModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<ResetPasswordModel> _logger;

        public ResetPasswordModel(UserManager<ApplicationUser> userManager, ILogger<ResetPasswordModel> logger)
        {
            _userManager = userManager;
            _logger = logger;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public bool LinkInvalid { get; set; }

        public class InputModel
        {
            [Required]
            public string UserId { get; set; } = string.Empty;

            [Required]
            public string Code { get; set; } = string.Empty;

            [Required]
            [EmailAddress]
            public string Email { get; set; } = string.Empty;

            [Required]
            [DataType(DataType.Password)]
            [Display(Name = "New password")]
            public string Password { get; set; } = string.Empty;

            [DataType(DataType.Password)]
            [Display(Name = "Confirm new password")]
            [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
            public string ConfirmPassword { get; set; } = string.Empty;
        }

        public async Task<IActionResult> OnGetAsync(string? code = null, string? userId = null)
        {
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(userId))
            {
                LinkInvalid = true;
                return Page();
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null || user.Email == null)
            {
                LinkInvalid = true;
                return Page();
            }

            if (!TryDecodeCode(code, out var decodedCode))
            {
                LinkInvalid = true;
                return Page();
            }

            var isValid = await _userManager.VerifyUserTokenAsync(
                user,
                _userManager.Options.Tokens.PasswordResetTokenProvider,
                "ResetPassword",
                decodedCode);

            if (!isValid)
            {
                LinkInvalid = true;
                return Page();
            }

            Input = new InputModel
            {
                Email = user.Email,
                Code = decodedCode,
                UserId = user.Id
            };

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            var user = await _userManager.FindByIdAsync(Input.UserId);
            if (user == null)
            {
                LinkInvalid = true;
                return Page();
            }

            var result = await _userManager.ResetPasswordAsync(user, Input.Code, Input.Password);
            if (result.Succeeded)
            {
                await _userManager.UpdateSecurityStampAsync(user);
                _logger.LogInformation("Password reset succeeded for user {UserId}.", user.Id);
                return RedirectToPage("./ResetPasswordConfirmation");
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            LinkInvalid = result.Errors.Any(error =>
                error.Code.Equals("InvalidToken", StringComparison.OrdinalIgnoreCase));

            return Page();
        }

        private static bool TryDecodeCode(string encodedCode, out string decodedCode)
        {
            try
            {
                decodedCode = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(encodedCode));
                return true;
            }
            catch
            {
                decodedCode = string.Empty;
                return false;
            }
        }
    }
}
