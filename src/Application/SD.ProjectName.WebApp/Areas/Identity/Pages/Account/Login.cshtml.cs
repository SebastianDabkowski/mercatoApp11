using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using SD.ProjectName.WebApp.Identity;

namespace SD.ProjectName.WebApp.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class LoginModel : PageModel
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<LoginModel> _logger;
        private readonly IEmailSender _emailSender;
        private readonly IOptions<KycOptions> _kycOptions;

        public LoginModel(
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            ILogger<LoginModel> logger,
            IEmailSender emailSender,
            IOptions<KycOptions> kycOptions)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _logger = logger;
            _emailSender = emailSender;
            _kycOptions = kycOptions;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public IList<AuthenticationScheme> ExternalLogins { get; set; } = new List<AuthenticationScheme>();

        public string? ReturnUrl { get; set; }

        [TempData]
        public string? ErrorMessage { get; set; }

        public bool ShowVerificationReminder { get; set; }

        public string? ResendConfirmationUrl { get; set; }

        public class InputModel
        {
            [Required]
            [EmailAddress]
            public string Email { get; set; } = string.Empty;

            [Required]
            [DataType(DataType.Password)]
            public string Password { get; set; } = string.Empty;

            [Display(Name = "Keep me signed in")]
            public bool RememberMe { get; set; } = true;
        }

        public async Task OnGetAsync(string? returnUrl = null)
        {
            if (!string.IsNullOrEmpty(ErrorMessage))
            {
                ModelState.AddModelError(string.Empty, ErrorMessage);
            }

            returnUrl ??= Url.Content("~/");

            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();

            ReturnUrl = returnUrl;
        }

        public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
        {
            returnUrl ??= Url.Content("~/");
            ReturnUrl = returnUrl;
            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();

            if (!ModelState.IsValid)
            {
                return Page();
            }

            var user = await _userManager.FindByEmailAsync(Input.Email);
            if (user != null)
            {
                if (user.AccountStatus == AccountStatuses.Unverified || !await _userManager.IsEmailConfirmedAsync(user))
                {
                    await SendVerificationEmailAsync(user, returnUrl);
                    ShowVerificationReminder = true;
                    ResendConfirmationUrl = Url.Page(
                        "/Account/ResendEmailConfirmation",
                        pageHandler: null,
                        values: new { area = "Identity", email = Input.Email },
                        protocol: Request.Scheme);
                    ModelState.AddModelError(string.Empty, GetVerificationMessage(user.AccountType));
                    _logger.LogInformation("Blocked login for unverified {Role} user {Email}.", user.AccountType, Input.Email);
                    return Page();
                }

                var result = await _signInManager.PasswordSignInAsync(
                    user.UserName!,
                    Input.Password,
                    Input.RememberMe,
                    lockoutOnFailure: true);

                if (result.Succeeded)
                {
                    _logger.LogInformation("User logged in.");
                    var redirectUrl = ResolveRedirectUrl(returnUrl, user);
                    return LocalRedirect(redirectUrl);
                }

                if (result.RequiresTwoFactor)
                {
                    return RedirectToPage("./LoginWith2fa", new { ReturnUrl = returnUrl, RememberMe = Input.RememberMe });
                }

                if (result.IsLockedOut)
                {
                    _logger.LogWarning("User account locked out.");
                    return RedirectToPage("./Lockout");
                }
            }

            ModelState.AddModelError(string.Empty, "Invalid login attempt.");
            _logger.LogWarning("Invalid login attempt for {Email}", Input.Email);
            return Page();
        }

        private async Task SendVerificationEmailAsync(ApplicationUser user, string? returnUrl)
        {
            var userId = await _userManager.GetUserIdAsync(user);
            var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
            var callbackUrl = Url.Page(
                "/Account/ConfirmEmail",
                pageHandler: null,
                values: new { area = "Identity", userId, code, returnUrl },
                protocol: Request.Scheme);

            await _emailSender.SendEmailAsync(Input.Email, "Verify your Mercato account",
                $"Please confirm your Mercato account so we can verify you: <a href='{HtmlEncoder.Default.Encode(callbackUrl!)}'>confirm email</a>.");
        }

        private static string GetVerificationMessage(string? accountType)
        {
            return accountType != null && accountType.Equals(AccountTypes.Seller, StringComparison.OrdinalIgnoreCase)
                ? "Seller accounts must verify email before logging in. We sent you a new verification link."
                : "Please verify your email before logging in. We sent you a new verification link.";
        }

        private string ResolveRedirectUrl(string? returnUrl, ApplicationUser user)
        {
            var homeUrl = Url?.Content("~/") ?? "/";

            if (!string.IsNullOrEmpty(returnUrl) &&
                !string.Equals(returnUrl, homeUrl, StringComparison.OrdinalIgnoreCase) &&
                (Url?.IsLocalUrl(returnUrl) ?? false))
            {
                return returnUrl;
            }

            if (ShouldRedirectToKyc(user))
            {
                return Url?.Content("~/Seller/Kyc") ?? "~/Seller/Kyc";
            }

            if (user.AccountType != null && user.AccountType.Equals(AccountTypes.Admin, StringComparison.OrdinalIgnoreCase))
            {
                return Url?.Content("~/Admin/Dashboard") ?? "~/Admin/Dashboard";
            }

            if (user.AccountType != null && user.AccountType.Equals(AccountTypes.Seller, StringComparison.OrdinalIgnoreCase))
            {
                return Url?.Content("~/Seller/Dashboard") ?? "~/Seller/Dashboard";
            }

            return Url?.Content("~/Buyer/Dashboard") ?? "~/Buyer/Dashboard";
        }

        private bool ShouldRedirectToKyc(ApplicationUser user)
        {
            if (user.AccountType == null || !user.AccountType.Equals(AccountTypes.Seller, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!_kycOptions.Value.RequireSellerKyc)
            {
                return false;
            }

            if (string.Equals(user.KycStatus, KycStatuses.Approved, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(user.KycStatus, KycStatuses.NotRequired, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return user.KycSubmittedOn == null;
        }
    }
}
