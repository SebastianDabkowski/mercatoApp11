using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using SD.ProjectName.WebApp.Identity;

namespace SD.ProjectName.WebApp.Areas.Identity.Pages.Account
{
    public class RegisterModel : PageModel
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IUserStore<ApplicationUser> _userStore;
        private readonly IUserEmailStore<ApplicationUser> _emailStore;
        private readonly ILogger<RegisterModel> _logger;
        private readonly IEmailSender _emailSender;
        private readonly IOptions<KycOptions> _kycOptions;

        public RegisterModel(
            UserManager<ApplicationUser> userManager,
            IUserStore<ApplicationUser> userStore,
            SignInManager<ApplicationUser> signInManager,
            ILogger<RegisterModel> logger,
            IEmailSender emailSender,
            IOptions<KycOptions> kycOptions)
        {
            _userManager = userManager;
            _userStore = userStore;
            _emailStore = GetEmailStore();
            _signInManager = signInManager;
            _logger = logger;
            _emailSender = emailSender;
            _kycOptions = kycOptions;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public IList<AuthenticationScheme> ExternalLogins { get; set; } = new List<AuthenticationScheme>();

        public string? ReturnUrl { get; set; }

        public class InputModel : IValidatableObject
        {
            [Required]
            [EmailAddress]
            [Display(Name = "Email")]
            public string Email { get; set; } = string.Empty;

            [Required]
            [Display(Name = "Full name")]
            [StringLength(256)]
            public string FullName { get; set; } = string.Empty;

            [Required]
            [Display(Name = "Street address")]
            [StringLength(512)]
            public string Address { get; set; } = string.Empty;

            [Required]
            [StringLength(100)]
            public string Country { get; set; } = string.Empty;

            [Display(Name = "Business name")]
            [StringLength(256)]
            public string? BusinessName { get; set; }

            [Display(Name = "Tax ID")]
            [StringLength(128)]
            public string? TaxId { get; set; }

            [Required]
            [Display(Name = "Account type")]
            public string AccountType { get; set; } = AccountTypes.Buyer;

            [Required]
            [StringLength(100, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 12)]
            [DataType(DataType.Password)]
            [Display(Name = "Password")]
            public string Password { get; set; } = string.Empty;

            [DataType(DataType.Password)]
            [Display(Name = "Confirm password")]
            [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
            public string ConfirmPassword { get; set; } = string.Empty;

            [Display(Name = "I accept the terms and privacy policy")]
            [Range(typeof(bool), "true", "true", ErrorMessage = "You must accept the terms to create an account.")]
            public bool AcceptTerms { get; set; }

            public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
            {
                if (!AccountTypes.IsValid(AccountType))
                {
                    yield return new ValidationResult("Choose a valid account type.", new[] { nameof(AccountType) });
                }

                if (AccountType.Equals(AccountTypes.Seller, StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(BusinessName))
                    {
                        yield return new ValidationResult("Business name is required for sellers.", new[] { nameof(BusinessName) });
                    }

                    if (string.IsNullOrWhiteSpace(TaxId))
                    {
                        yield return new ValidationResult("Tax ID is required for sellers.", new[] { nameof(TaxId) });
                    }
                }
            }
        }

        public async Task OnGetAsync(string? returnUrl = null)
        {
            ReturnUrl = returnUrl ?? Url.Content("~/");
            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();
        }

        public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
        {
            returnUrl ??= Url.Content("~/");
            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();

            if (ModelState.IsValid)
            {
                var user = CreateUser();
                user.FullName = Input.FullName;
                user.Address = Input.Address;
                user.Country = Input.Country;
                user.BusinessName = Input.BusinessName;
                user.TaxId = Input.TaxId;
                var normalizedAccountType = AccountTypes.Allowed.FirstOrDefault(a => a.Equals(Input.AccountType, StringComparison.OrdinalIgnoreCase))
                                           ?? AccountTypes.Buyer;
                user.AccountType = normalizedAccountType;
                user.AccountStatus = AccountStatuses.Unverified;
                user.KycStatus = normalizedAccountType == AccountTypes.Seller && _kycOptions.Value.RequireSellerKyc
                    ? KycStatuses.Pending
                    : KycStatuses.NotRequired;
                if (normalizedAccountType == AccountTypes.Seller)
                {
                    user.OnboardingStatus = OnboardingStatuses.InProgress;
                    user.OnboardingStartedOn = DateTimeOffset.UtcNow;
                    user.OnboardingStep = 0;
                }
                else
                {
                    user.OnboardingStatus = OnboardingStatuses.Completed;
                }
                user.TermsAccepted = Input.AcceptTerms;
                user.TermsAcceptedOn = DateTimeOffset.UtcNow;

                await _userStore.SetUserNameAsync(user, Input.Email, CancellationToken.None);
                await _emailStore.SetEmailAsync(user, Input.Email, CancellationToken.None);
                var result = await _userManager.CreateAsync(user, Input.Password);

                if (result.Succeeded)
                {
                    _logger.LogInformation("User created a new account with password.");

                    await _userManager.AddToRoleAsync(user, user.AccountType);

                    var userId = await _userManager.GetUserIdAsync(user);
                    var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
                    code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
                    var callbackUrl = Url.Page(
                        "/Account/ConfirmEmail",
                        pageHandler: null,
                        values: new { area = "Identity", userId, code, returnUrl },
                        protocol: Request.Scheme);

                    await _emailSender.SendEmailAsync(Input.Email, "Confirm your email",
                        $"Please confirm your Mercato account so we can verify you: <a href='{HtmlEncoder.Default.Encode(callbackUrl!)}'>confirm email</a>.");

                    return RedirectToPage("RegisterConfirmation", new { email = Input.Email, returnUrl });
                }

                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
            }

            return Page();
        }

        private ApplicationUser CreateUser()
        {
            try
            {
                return Activator.CreateInstance<ApplicationUser>()!;
            }
            catch
            {
                throw new InvalidOperationException($"Can't create an instance of '{nameof(ApplicationUser)}'. " +
                    $"Ensure that it is not abstract and has a parameterless constructor.");
            }
        }

        private IUserEmailStore<ApplicationUser> GetEmailStore()
        {
            if (!_userManager.SupportsUserEmail)
            {
                throw new NotSupportedException("The default UI requires a user store with email support.");
            }

            return (IUserEmailStore<ApplicationUser>)_userStore;
        }
    }
}
