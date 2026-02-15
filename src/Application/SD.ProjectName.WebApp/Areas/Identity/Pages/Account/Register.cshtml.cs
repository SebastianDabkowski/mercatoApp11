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
using SD.ProjectName.WebApp.Services;

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
        private readonly EmailOptions _emailOptions;
        private readonly ILegalDocumentService _legalDocuments;
        private readonly IConsentService _consents;

        public RegisterModel(
            UserManager<ApplicationUser> userManager,
            IUserStore<ApplicationUser> userStore,
            SignInManager<ApplicationUser> signInManager,
            ILogger<RegisterModel> logger,
            IEmailSender emailSender,
            IOptions<KycOptions> kycOptions,
            IOptions<EmailOptions> emailOptions,
            ILegalDocumentService legalDocuments,
            IConsentService consents)
        {
            _userManager = userManager;
            _userStore = userStore;
            _emailStore = GetEmailStore();
            _signInManager = signInManager;
            _logger = logger;
            _emailSender = emailSender;
            _kycOptions = kycOptions;
            _emailOptions = emailOptions.Value;
            _legalDocuments = legalDocuments;
            _consents = consents;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public IList<AuthenticationScheme> ExternalLogins { get; set; } = new List<AuthenticationScheme>();

        public string? ReturnUrl { get; set; }

        public LegalDocumentVersion? ActiveTerms { get; private set; }

        public List<ConsentDisplay> Consents { get; private set; } = new();

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

            public List<ConsentInputModel> Consents { get; set; } = new();

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

        public class ConsentInputModel
        {
            [Required]
            public string Type { get; set; } = string.Empty;

            [Display(Name = "I agree")]
            public bool Accepted { get; set; }
        }

        public class ConsentDisplay
        {
            public string Type { get; set; } = string.Empty;

            public string Title { get; set; } = string.Empty;

            public string Description { get; set; } = string.Empty;

            public string VersionTag { get; set; } = string.Empty;

            public DateTimeOffset? VersionEffectiveFrom { get; set; }

            public bool AllowPreselect { get; set; }

            public bool IsRequired { get; set; }

            public string Content { get; set; } = string.Empty;

            public bool Accepted { get; set; }
        }

        public async Task OnGetAsync(string? returnUrl = null)
        {
            ReturnUrl = returnUrl ?? Url.Content("~/");
            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();
            await LoadLegalAsync();
            await LoadConsentsAsync();
        }

        public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
        {
            returnUrl ??= Url.Content("~/");
            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();
            await LoadLegalAsync();
            await LoadConsentsAsync();
            ValidateConsentSelections();

            if (ModelState.IsValid)
            {
                var activeTerms = ActiveTerms ?? await _legalDocuments.GetActiveVersionAsync(LegalDocumentTypes.TermsOfService, DateTimeOffset.UtcNow, HttpContext.RequestAborted);
                if (activeTerms == null)
                {
                    ModelState.AddModelError(string.Empty, "Terms of Service are not configured yet. Please contact support.");
                    return Page();
                }

                var user = CreateUser();
                user.FullName = Input.FullName;
                user.Address = Input.Address;
                user.Country = Input.Country;
                user.BusinessName = Input.BusinessName;
                user.TaxId = Input.TaxId;
                user.ContactEmail = Input.Email;
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
                user.TermsVersionId = activeTerms.Id;

                await _userStore.SetUserNameAsync(user, Input.Email, CancellationToken.None);
                await _emailStore.SetEmailAsync(user, Input.Email, CancellationToken.None);
                var result = await _userManager.CreateAsync(user, Input.Password);

                if (result.Succeeded)
                {
                    _logger.LogInformation("User created a new account with password.");

                    await _userManager.AddToRoleAsync(user, user.AccountType);
                    if (normalizedAccountType == AccountTypes.Seller)
                    {
                        await _userManager.AddToRoleAsync(user, SellerInternalRoles.StoreOwner);
                        user.StoreOwnerId = user.Id;
                        await _userManager.UpdateAsync(user);
                    }

                    var consentResult = await _consents.RecordUserConsentsAsync(
                        user.Id,
                        Input.Consents.ToDictionary(c => c.Type, c => c.Accepted),
                        HttpContext.RequestAborted);
                    if (!consentResult.Success)
                    {
                        foreach (var error in consentResult.Errors)
                        {
                            ModelState.AddModelError(string.Empty, error);
                        }
                        return Page();
                    }

                    var userId = await _userManager.GetUserIdAsync(user);
                    var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
                    code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
                    var callbackUrl = Url.Page(
                        "/Account/ConfirmEmail",
                        pageHandler: null,
                        values: new { area = "Identity", userId, code, returnUrl },
                        protocol: Request.Scheme);

                    var body = $"<p>Welcome to Mercato, {user.FullName}!</p><p>Please confirm your account so we can verify you: <a href='{HtmlEncoder.Default.Encode(callbackUrl!)}'>confirm email</a>.</p>";
                    await SendRegistrationEmailAsync(Input.Email, "Confirm your email", body);

                    return RedirectToPage("RegisterConfirmation", new { email = Input.Email, returnUrl });
                }

                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
            }

            return Page();
        }

        private async Task LoadLegalAsync()
        {
            ActiveTerms = await _legalDocuments.GetActiveVersionAsync(LegalDocumentTypes.TermsOfService, DateTimeOffset.UtcNow, HttpContext.RequestAborted);
        }

        private async Task LoadConsentsAsync()
        {
            Input ??= new InputModel();
            Input.Consents ??= new List<ConsentInputModel>();

            var definitions = await _consents.GetActiveConsentsAsync(HttpContext.RequestAborted);
            var inputLookup = Input.Consents.ToDictionary(c => ConsentTypes.Normalize(c.Type), c => c, StringComparer.OrdinalIgnoreCase);

            Consents = new List<ConsentDisplay>();
            foreach (var definition in definitions)
            {
                var normalizedType = ConsentTypes.Normalize(definition.ConsentType);
                var accepted = inputLookup.TryGetValue(normalizedType, out var selection)
                    ? selection.Accepted
                    : definition.AllowPreselect;

                if (!inputLookup.ContainsKey(normalizedType))
                {
                    Input.Consents.Add(new ConsentInputModel
                    {
                        Type = normalizedType,
                        Accepted = accepted
                    });
                }

                Consents.Add(new ConsentDisplay
                {
                    Type = normalizedType,
                    Title = definition.Title,
                    Description = definition.Description,
                    VersionTag = definition.ActiveVersion?.VersionTag ?? "n/a",
                    VersionEffectiveFrom = definition.ActiveVersion?.EffectiveFrom,
                    AllowPreselect = definition.AllowPreselect,
                    IsRequired = definition.IsRequired,
                    Content = definition.ActiveVersion?.Content ?? string.Empty,
                    Accepted = accepted
                });
            }
        }

        private void ValidateConsentSelections()
        {
            foreach (var consent in Consents.Where(c => c.IsRequired))
            {
                var selection = Input.Consents.FirstOrDefault(c => ConsentTypes.Normalize(c.Type) == consent.Type);
                if (selection == null || !selection.Accepted)
                {
                    ModelState.AddModelError(string.Empty, $"You must accept {consent.Title} to continue.");
                }
            }
        }

        private async Task SendRegistrationEmailAsync(string email, string subject, string body)
        {
            _logger.LogInformation("Sending registration email to {Email} from {Sender}", email, _emailOptions.FromAddress);
            try
            {
                var wrappedBody = EmailTemplateBuilder.Wrap(subject, body, _emailOptions);
                await _emailSender.SendEmailAsync(email, subject, wrappedBody);
                _logger.LogInformation("Registration email sent to {Email}", email);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send registration email to {Email}", email);
            }
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
