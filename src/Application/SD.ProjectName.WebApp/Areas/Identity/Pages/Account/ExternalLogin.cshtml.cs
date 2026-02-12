using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.WebApp.Identity;

namespace SD.ProjectName.WebApp.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class ExternalLoginModel : PageModel
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<ExternalLoginModel> _logger;

        public ExternalLoginModel(
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            ILogger<ExternalLoginModel> logger)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _logger = logger;
        }

        [TempData]
        public string? ErrorMessage { get; set; }

        public string? ReturnUrl { get; set; }

        public IActionResult OnPost(string provider, string? returnUrl = null)
        {
            var redirectUrl = Url.Page("./ExternalLogin", pageHandler: "Callback", values: new { returnUrl });
            var properties = _signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
            return new ChallengeResult(provider, properties);
        }

        public async Task<IActionResult> OnGetCallbackAsync(string? returnUrl = null, string? remoteError = null)
        {
            ReturnUrl = returnUrl ?? Url.Content("~/");

            if (!string.IsNullOrEmpty(remoteError))
            {
                ErrorMessage = $"Social login failed: {remoteError}";
                _logger.LogWarning("External login failed with remote error: {RemoteError}", remoteError);
                return RedirectToPage("./Login", new { ReturnUrl });
            }

            var info = await _signInManager.GetExternalLoginInfoAsync();
            if (info == null)
            {
                ErrorMessage = "Error loading external login information.";
                _logger.LogWarning("External login info was null during callback.");
                return RedirectToPage("./Login", new { ReturnUrl });
            }

            var existingLoginUser = await _userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
            if (existingLoginUser != null)
            {
                if (!IsBuyer(existingLoginUser))
                {
                    return RejectForNonBuyer(ReturnUrl);
                }

                await EnsureVerifiedAsync(existingLoginUser);
                await _signInManager.SignInAsync(existingLoginUser, isPersistent: false, info.LoginProvider);
                await _signInManager.UpdateExternalAuthenticationTokensAsync(info);
                _logger.LogInformation("User {UserId} signed in with {Provider}.", existingLoginUser.Id, info.LoginProvider);
                return LocalRedirect(ResolveRedirectUrl(ReturnUrl, existingLoginUser.AccountType));
            }

            var email = info.Principal.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrWhiteSpace(email))
            {
                ErrorMessage = "We couldn't retrieve your email from the provider.";
                _logger.LogWarning("External login missing email claim for provider {Provider}.", info.LoginProvider);
                return RedirectToPage("./Login", new { ReturnUrl });
            }

            var user = await _userManager.FindByEmailAsync(email);
            if (user != null && !IsBuyer(user))
            {
                return RejectForNonBuyer(ReturnUrl);
            }

            if (user == null)
            {
                user = new ApplicationUser
                {
                    UserName = email,
                    Email = email,
                    FullName = info.Principal.FindFirstValue(ClaimTypes.Name) ?? email,
                    AccountType = AccountTypes.Buyer,
                    AccountStatus = AccountStatuses.Verified,
                    EmailConfirmed = true,
                    TermsAccepted = true,
                    TermsAcceptedOn = DateTimeOffset.UtcNow
                };

                var createResult = await _userManager.CreateAsync(user);
                if (!createResult.Succeeded)
                {
                    ErrorMessage = "We couldn't create your Mercato buyer account.";
                    _logger.LogWarning("Failed to create user from external login {Provider}: {Errors}", info.LoginProvider, string.Join(',', createResult.Errors.Select(e => e.Description)));
                    return RedirectToPage("./Login", new { ReturnUrl });
                }

                await _userManager.AddToRoleAsync(user, AccountTypes.Buyer);
            }
            else
            {
                await EnsureVerifiedAsync(user);
            }

            var addLoginResult = await _userManager.AddLoginAsync(user, info);
            if (!addLoginResult.Succeeded)
            {
                ErrorMessage = "We couldn't link your social account. Please try email and password instead.";
                _logger.LogWarning("Failed to link external login for user {UserId}: {Errors}", user.Id, string.Join(',', addLoginResult.Errors.Select(e => e.Description)));
                return RedirectToPage("./Login", new { ReturnUrl });
            }

            await _signInManager.SignInAsync(user, isPersistent: false, info.LoginProvider);
            await _signInManager.UpdateExternalAuthenticationTokensAsync(info);
            _logger.LogInformation("User {UserId} created/logged in using {Provider}.", user.Id, info.LoginProvider);
            return LocalRedirect(ResolveRedirectUrl(ReturnUrl, user.AccountType));
        }

        private async Task EnsureVerifiedAsync(ApplicationUser user)
        {
            var changed = false;

            if (!user.EmailConfirmed)
            {
                user.EmailConfirmed = true;
                changed = true;
            }

            if (!string.Equals(user.AccountStatus, AccountStatuses.Verified, StringComparison.OrdinalIgnoreCase))
            {
                user.AccountStatus = AccountStatuses.Verified;
                changed = true;
            }

            if (!user.TermsAccepted)
            {
                user.TermsAccepted = true;
                user.TermsAcceptedOn = DateTimeOffset.UtcNow;
                changed = true;
            }

            if (changed)
            {
                await _userManager.UpdateAsync(user);
            }
        }

        private IActionResult RejectForNonBuyer(string? returnUrl)
        {
            ErrorMessage = "Social login is currently available for buyers only.";
            _logger.LogWarning("Blocked external login for non-buyer account.");
            return RedirectToPage("./Login", new { ReturnUrl = returnUrl });
        }

        private static bool IsBuyer(ApplicationUser user) =>
            user.AccountType.Equals(AccountTypes.Buyer, StringComparison.OrdinalIgnoreCase);

        private string ResolveRedirectUrl(string? returnUrl, string? accountType)
        {
            var homeUrl = Url?.Content("~/") ?? "/";

            if (!string.IsNullOrEmpty(returnUrl) &&
                !string.Equals(returnUrl, homeUrl, StringComparison.OrdinalIgnoreCase) &&
                (Url?.IsLocalUrl(returnUrl) ?? false))
            {
                return returnUrl;
            }

            if (accountType != null && accountType.Equals(AccountTypes.Seller, StringComparison.OrdinalIgnoreCase))
            {
                return Url?.Content("~/Seller/Dashboard") ?? "~/Seller/Dashboard";
            }

            return Url?.Content("~/Buyer/Dashboard") ?? "~/Buyer/Dashboard";
        }
    }
}
