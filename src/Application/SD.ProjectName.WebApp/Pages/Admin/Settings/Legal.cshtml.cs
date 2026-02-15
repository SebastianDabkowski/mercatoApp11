using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages.Admin.Settings
{
    [Authorize(Policy = Permissions.AdminSettings)]
    public class LegalModel : PageModel
    {
        private readonly ILegalDocumentService _legalDocuments;

        public LegalModel(ILegalDocumentService legalDocuments)
        {
            _legalDocuments = legalDocuments;
        }

        [BindProperty]
        public LegalForm Input { get; set; } = new();

        public List<LegalDocumentVersion> Versions { get; private set; } = new();

        public LegalDocumentVersion? ActiveVersion { get; private set; }

        public LegalDocumentVersion? UpcomingVersion { get; private set; }

        public List<string> Errors { get; private set; } = new();

        [TempData]
        public string? SuccessMessage { get; set; }

        public string[] DocumentTypes => LegalDocumentTypes.Allowed;

        public async Task OnGetAsync(string? type = null, int? id = null)
        {
            var selectedType = LegalDocumentTypes.Normalize(type ?? Input.DocumentType);
            await LoadAsync(selectedType, id);
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var selectedType = LegalDocumentTypes.Normalize(Input.DocumentType);
            if (!ModelState.IsValid)
            {
                await LoadAsync(selectedType, Input.Id);
                return Page();
            }

            var actorId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            var actorName = User.Identity?.Name ?? "Admin";
            var input = new LegalDocumentInput
            {
                Id = Input.Id,
                DocumentType = selectedType,
                VersionTag = Input.VersionTag,
                Title = Input.Title,
                Content = Input.Content ?? string.Empty,
                EffectiveFrom = DateTime.SpecifyKind(Input.EffectiveFrom, DateTimeKind.Utc)
            };

            var result = await _legalDocuments.SaveAsync(input, actorId, actorName, HttpContext.RequestAborted);
            if (!result.Success)
            {
                Errors = result.Errors;
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error);
                }

                await LoadAsync(selectedType, Input.Id);
                return Page();
            }

            SuccessMessage = Input.Id.HasValue ? "Legal document updated." : "New legal version saved.";
            return RedirectToPage("/Admin/Settings/Legal", new { type = selectedType, id = result.Version?.Id });
        }

        private async Task LoadAsync(string selectedType, int? editId)
        {
            var now = DateTimeOffset.UtcNow;
            Input.DocumentType = selectedType;
            Versions = await _legalDocuments.GetVersionsAsync(selectedType, HttpContext.RequestAborted);
            ActiveVersion = await _legalDocuments.GetActiveVersionAsync(selectedType, now, HttpContext.RequestAborted);
            UpcomingVersion = await _legalDocuments.GetUpcomingVersionAsync(selectedType, now, HttpContext.RequestAborted);

            if (editId.HasValue)
            {
                var match = await _legalDocuments.GetVersionAsync(editId.Value, HttpContext.RequestAborted);
                if (match != null)
                {
                    Input = new LegalForm
                    {
                        Id = match.Id,
                        DocumentType = match.DocumentType,
                        VersionTag = match.VersionTag,
                        Title = match.Title,
                        EffectiveFrom = match.EffectiveFrom.UtcDateTime,
                        Content = match.Content
                    };
                    return;
                }
            }

            if (Input.Id == null && Input.EffectiveFrom == default)
            {
                Input.EffectiveFrom = DateTime.UtcNow.Date;
            }
        }

        public class LegalForm
        {
            public int? Id { get; set; }

            [Required]
            [MaxLength(64)]
            [Display(Name = "Document type")]
            public string DocumentType { get; set; } = LegalDocumentTypes.TermsOfService;

            [MaxLength(64)]
            [Display(Name = "Version tag")]
            public string? VersionTag { get; set; }

            [MaxLength(256)]
            [Display(Name = "Title (optional)")]
            public string? Title { get; set; }

            [Required]
            [DataType(DataType.Date)]
            [Display(Name = "Effective from")]
            public DateTime EffectiveFrom { get; set; } = DateTime.UtcNow.Date;

            [Required]
            [Display(Name = "Content (HTML supported)")]
            public string Content { get; set; } = string.Empty;
        }
    }
}
