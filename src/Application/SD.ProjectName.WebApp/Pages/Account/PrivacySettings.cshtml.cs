using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages.Account
{
    [Authorize]
    public class PrivacySettingsModel : PageModel
    {
        private readonly IConsentService _consents;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly ILogger<PrivacySettingsModel> _logger;
        private readonly UserDataExportService _dataExport;
        private readonly UserAccountDeletionService _deletionService;

        public PrivacySettingsModel(
            IConsentService consents,
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            UserDataExportService dataExport,
            UserAccountDeletionService deletionService,
            ILogger<PrivacySettingsModel> logger)
        {
            _consents = consents;
            _userManager = userManager;
            _signInManager = signInManager;
            _dataExport = dataExport;
            _deletionService = deletionService;
            _logger = logger;
        }

        [BindProperty]
        public List<ConsentInputModel> Consents { get; set; } = new();

        [BindProperty]
        public bool ConfirmDeletion { get; set; }

        public List<ConsentViewModel> ActiveConsents { get; private set; } = new();

        [TempData]
        public string? StatusMessage { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            await LoadAsync(user, preserveSelections: false);
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            await LoadAsync(user, preserveSelections: true);

            if (!ModelState.IsValid)
            {
                return Page();
            }

            var result = await _consents.RecordUserConsentsAsync(
                user.Id,
                Consents.ToDictionary(c => c.Type, c => c.Accepted),
                HttpContext.RequestAborted);

            if (!result.Success)
            {
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error);
                }

                await LoadAsync(user, preserveSelections: true);
                return Page();
            }

            StatusMessage = "Consent preferences saved.";
            _logger.LogInformation("User {UserId} updated consent preferences.", user.Id);
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostExportAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            try
            {
                var result = await _dataExport.GenerateAsync(user.Id, HttpContext.RequestAborted);
                return File(result.Content, result.ContentType, result.FileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate data export for user {UserId}", user.Id);
                await LoadAsync(user, preserveSelections: true);
                ModelState.AddModelError(string.Empty, "We could not generate your data export right now. Please try again later.");
                return Page();
            }
        }

        public async Task<IActionResult> OnPostDeleteAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            await LoadAsync(user, preserveSelections: true);

            if (!ConfirmDeletion)
            {
                ModelState.AddModelError(nameof(ConfirmDeletion), "Please confirm you understand the impact before deleting your account.");
                return Page();
            }

            var result = await _deletionService.DeleteAsync(
                user.Id,
                user.Id,
                "User self-service",
                HttpContext.RequestAborted);

            if (!result.Success)
            {
                if (result.BlockingReasons != null && result.BlockingReasons.Count > 0)
                {
                    foreach (var reason in result.BlockingReasons)
                    {
                        ModelState.AddModelError(string.Empty, reason);
                    }
                }
                else if (!string.IsNullOrWhiteSpace(result.Error))
                {
                    ModelState.AddModelError(string.Empty, result.Error);
                }

                return Page();
            }

            await _signInManager.SignOutAsync();
            StatusMessage = "Your account has been deleted and your personal data has been anonymized.";
            return RedirectToPage("/Index");
        }

        private async Task LoadAsync(ApplicationUser user, bool preserveSelections)
        {
            Consents ??= new List<ConsentInputModel>();

            var definitions = await _consents.GetActiveConsentsAsync(HttpContext.RequestAborted);
            var snapshots = await _consents.GetUserConsentsAsync(user.Id, HttpContext.RequestAborted);
            var selectionLookup = preserveSelections
                ? Consents.ToDictionary(c => ConsentTypes.Normalize(c.Type), c => c, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, ConsentInputModel>(StringComparer.OrdinalIgnoreCase);

            ActiveConsents = new List<ConsentViewModel>();
            Consents = new List<ConsentInputModel>();

            foreach (var definition in definitions)
            {
                var normalized = ConsentTypes.Normalize(definition.ConsentType);
                var snapshot = snapshots.FirstOrDefault(s => ConsentTypes.Normalize(s.ConsentType) == normalized);
                var accepted = selectionLookup.TryGetValue(normalized, out var chosen)
                    ? chosen.Accepted
                    : snapshot?.Granted ?? definition.AllowPreselect;

                Consents.Add(new ConsentInputModel
                {
                    Type = normalized,
                    Accepted = accepted
                });

                ActiveConsents.Add(new ConsentViewModel
                {
                    Type = normalized,
                    Title = definition.Title,
                    Description = definition.Description,
                    VersionTag = definition.ActiveVersion?.VersionTag ?? "n/a",
                    VersionEffectiveFrom = definition.ActiveVersion?.EffectiveFrom,
                    Accepted = accepted,
                    LastUpdatedOn = snapshot?.DecidedOn,
                    LastVersionTag = snapshot?.Version.VersionTag
                });
            }
        }

        public class ConsentInputModel
        {
            [Required]
            public string Type { get; set; } = string.Empty;

            public bool Accepted { get; set; }
        }

        public class ConsentViewModel
        {
            public string Type { get; set; } = string.Empty;

            public string Title { get; set; } = string.Empty;

            public string Description { get; set; } = string.Empty;

            public string VersionTag { get; set; } = string.Empty;

            public DateTimeOffset? VersionEffectiveFrom { get; set; }

            public bool Accepted { get; set; }

            public DateTimeOffset? LastUpdatedOn { get; set; }

            public string? LastVersionTag { get; set; }
        }
    }
}
