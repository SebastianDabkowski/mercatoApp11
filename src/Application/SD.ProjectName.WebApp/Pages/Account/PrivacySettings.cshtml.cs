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
        private readonly ILogger<PrivacySettingsModel> _logger;

        public PrivacySettingsModel(
            IConsentService consents,
            UserManager<ApplicationUser> userManager,
            ILogger<PrivacySettingsModel> logger)
        {
            _consents = consents;
            _userManager = userManager;
            _logger = logger;
        }

        [BindProperty]
        public List<ConsentInputModel> Consents { get; set; } = new();

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
