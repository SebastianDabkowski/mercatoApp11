using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SD.ProjectName.WebApp.Data;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages.Seller
{
    public class AcceptInvitationModel : PageModel
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IOptions<SellerInternalUserOptions> _featureOptions;
        private readonly ILogger<AcceptInvitationModel> _logger;
        private readonly ILegalDocumentService _legalDocuments;

        public AcceptInvitationModel(
            ApplicationDbContext dbContext,
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            IOptions<SellerInternalUserOptions> featureOptions,
            ILogger<AcceptInvitationModel> logger,
            ILegalDocumentService legalDocuments)
        {
            _dbContext = dbContext;
            _userManager = userManager;
            _signInManager = signInManager;
            _featureOptions = featureOptions;
            _logger = logger;
            _legalDocuments = legalDocuments;
        }

        public string InvitationEmail { get; private set; } = string.Empty;
        public string InvitationRole { get; private set; } = string.Empty;

        public LegalDocumentVersion? ActiveTerms { get; private set; }

        [BindProperty(SupportsGet = true)]
        public string? Code { get; set; }

        [BindProperty]
        public AcceptInput Input { get; set; } = new();

        public class AcceptInput
        {
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

            [Required]
            [StringLength(100, MinimumLength = 12)]
            [DataType(DataType.Password)]
            public string Password { get; set; } = string.Empty;

            [DataType(DataType.Password)]
            [Compare(nameof(Password))]
            public string ConfirmPassword { get; set; } = string.Empty;

            [Display(Name = "I accept the terms and privacy policy")]
            [Range(typeof(bool), "true", "true", ErrorMessage = "You must accept the terms to continue.")]
            public bool AcceptTerms { get; set; }
        }

        public async Task<IActionResult> OnGetAsync()
        {
            if (!_featureOptions.Value.Enabled)
            {
                return NotFound();
            }

            var invite = await LoadInvitationAsync();
            if (invite == null)
            {
                return NotFound();
            }

            InvitationEmail = invite.Email;
            InvitationRole = invite.Role;
            ActiveTerms = await _legalDocuments.GetActiveVersionAsync(LegalDocumentTypes.TermsOfService, DateTimeOffset.UtcNow, HttpContext.RequestAborted);
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!_featureOptions.Value.Enabled)
            {
                return NotFound();
            }

            var invite = await LoadInvitationAsync();
            if (invite == null)
            {
                ModelState.AddModelError(string.Empty, "Invitation not found or expired.");
                return Page();
            }

            InvitationEmail = invite.Email;
            InvitationRole = invite.Role;

            if (!ModelState.IsValid)
            {
                return Page();
            }

            var activeTerms = await _legalDocuments.GetActiveVersionAsync(LegalDocumentTypes.TermsOfService, DateTimeOffset.UtcNow, HttpContext.RequestAborted);
            if (activeTerms == null)
            {
                ActiveTerms = activeTerms;
                ModelState.AddModelError(string.Empty, "Terms of Service are not configured. Contact support.");
                return Page();
            }
            ActiveTerms = activeTerms;

            var existing = await _userManager.FindByEmailAsync(invite.Email);
            if (existing != null)
            {
                ModelState.AddModelError(string.Empty, "An account already exists for this email. Please sign in instead.");
                return Page();
            }

            var user = new ApplicationUser
            {
                Email = invite.Email,
                UserName = invite.Email,
                AccountType = AccountTypes.Seller,
                AccountStatus = AccountStatuses.Verified,
                EmailConfirmed = true,
                StoreOwnerId = invite.StoreOwnerId,
                FullName = Input.FullName,
                Address = Input.Address,
                Country = Input.Country,
                ContactEmail = invite.Email,
                KycStatus = KycStatuses.NotRequired,
                OnboardingStatus = OnboardingStatuses.Completed,
                OnboardingStep = 3,
                TermsAccepted = Input.AcceptTerms,
                TermsAcceptedOn = DateTimeOffset.UtcNow,
                TermsVersionId = activeTerms.Id
            };

            var result = await _userManager.CreateAsync(user, Input.Password);
            if (result.Succeeded)
            {
                await _userManager.AddToRoleAsync(user, AccountTypes.Seller);
                await _userManager.AddToRoleAsync(user, invite.Role);

                invite.Status = SellerInternalUserStatuses.Active;
                invite.AcceptedOn = DateTimeOffset.UtcNow;
                invite.AcceptedUserId = user.Id;
                await _dbContext.SaveChangesAsync();

                await _signInManager.SignInAsync(user, isPersistent: false);
                return RedirectToPage("/Seller/Dashboard");
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return Page();
        }

        private Task<SellerTeamMember?> LoadInvitationAsync()
        {
            if (string.IsNullOrWhiteSpace(Code))
            {
                return Task.FromResult<SellerTeamMember?>(null);
            }

            return _dbContext.SellerTeamMembers
                .FirstOrDefaultAsync(m => m.InvitationCode == Code &&
                                          m.Status == SellerInternalUserStatuses.Pending);
        }
    }
}
