using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class LoginWith2faModel : PageModel
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<LoginWith2faModel> _logger;
        private readonly IEmailSender _emailSender;
        private readonly IOptions<SecurityOptions> _securityOptions;
        private readonly ILoginAuditService _loginAuditService;
        private readonly IUserCartService _userCartService;

        public LoginWith2faModel(
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            ILogger<LoginWith2faModel> logger,
            IEmailSender emailSender,
            IOptions<SecurityOptions> securityOptions,
            ILoginAuditService loginAuditService,
            IUserCartService userCartService)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _logger = logger;
            _emailSender = emailSender;
            _securityOptions = securityOptions;
            _loginAuditService = loginAuditService;
            _userCartService = userCartService;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public string? ReturnUrl { get; set; }

        public bool RememberMe { get; set; }

        public class InputModel
        {
            [Required]
            [StringLength(32, MinimumLength = 6)]
            [Display(Name = "Verification code")]
            public string Code { get; set; } = string.Empty;

            [Display(Name = "Remember this machine")]
            public bool RememberMachine { get; set; }
        }

        public async Task<IActionResult> OnGetAsync(bool rememberMe, string? returnUrl = null)
        {
            returnUrl ??= Url.Content("~/");
            var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();

            if (user == null)
            {
                return NotFound("Unable to load two-factor authentication user.");
            }

            if (await _userManager.IsLockedOutAsync(user))
            {
                ModelState.AddModelError(string.Empty, BuildLockedOutMessage(user));
                return Page();
            }

            ReturnUrl = returnUrl;
            RememberMe = rememberMe;

            await SendCodeAsync(user);
            return Page();
        }

        public async Task<IActionResult> OnPostAsync(bool rememberMe, string? returnUrl = null)
        {
            returnUrl ??= Url.Content("~/");
            ReturnUrl = returnUrl;
            RememberMe = rememberMe;

            if (!ModelState.IsValid)
            {
                return Page();
            }

            var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
            if (user == null)
            {
                return NotFound("Unable to load two-factor authentication user.");
            }

            if (await _userManager.IsLockedOutAsync(user))
            {
                ModelState.AddModelError(string.Empty, BuildLockedOutMessage(user));
                await LogAuditAsync(user, LoginEventTypes.LockedOut, false);
                return Page();
            }

            var code = Input.Code.Replace(" ", string.Empty, StringComparison.Ordinal)
                .Replace("-", string.Empty, StringComparison.Ordinal);

            var result = await _signInManager.TwoFactorSignInAsync(
                _securityOptions.Value.TwoFactorProvider,
                code,
                rememberMe,
                Input.RememberMachine);

            if (result.Succeeded)
            {
                _logger.LogInformation("User logged in with 2fa.");
                await LogAuditAsync(user, LoginEventTypes.TwoFactorSuccess, true);
                await _userCartService.MergeOnSignInAsync(HttpContext, user, HttpContext.RequestAborted);
                return LocalRedirect(returnUrl);
            }

            if (result.IsLockedOut)
            {
                _logger.LogWarning("User account locked out after 2fa.");
                await LogAuditAsync(user, LoginEventTypes.LockedOut, false);
                ModelState.AddModelError(string.Empty, BuildLockedOutMessage(user));
                return Page();
            }

            _logger.LogWarning("Invalid authenticator code entered for user {UserId}.", user.Id);
            await LogAuditAsync(user, LoginEventTypes.TwoFactorFailed, false);
            ModelState.AddModelError(string.Empty, "Invalid verification code.");
            return Page();
        }

        private async Task SendCodeAsync(ApplicationUser user)
        {
            if (string.IsNullOrWhiteSpace(user.Email))
            {
                return;
            }

            var code = await _userManager.GenerateTwoFactorTokenAsync(user, _securityOptions.Value.TwoFactorProvider);
            await _emailSender.SendEmailAsync(
                user.Email,
                "Your login verification code",
                $"Your security code is: {code}");
        }

        private Task LogAuditAsync(ApplicationUser user, string eventType, bool isSuccess)
        {
            return _loginAuditService.RecordAsync(new LoginAuditEntry
            {
                UserId = user.Id,
                Email = user.Email,
                EventType = eventType,
                IsSuccess = isSuccess,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = Request.Headers.UserAgent.ToString()
            });
        }

        private static string BuildLockedOutMessage(ApplicationUser user)
        {
            if (user.LockoutEnd.HasValue && user.LockoutEnd.Value == DateTimeOffset.MaxValue)
            {
                return string.IsNullOrWhiteSpace(user.BlockReason)
                    ? "Your account has been blocked. Please contact support."
                    : $"Your account has been blocked: {user.BlockReason}";
            }

            if (user.LockoutEnd.HasValue && user.LockoutEnd.Value >= DateTimeOffset.UtcNow)
            {
                return $"Account locked due to too many attempts. Try again after {user.LockoutEnd.Value.ToLocalTime():g}.";
            }

            return "Account is locked. Please try again later.";
        }
    }
}
