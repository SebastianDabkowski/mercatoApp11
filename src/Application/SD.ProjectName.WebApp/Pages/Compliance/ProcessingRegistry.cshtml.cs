using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages.Compliance;

[Authorize(Policy = Permissions.ComplianceRegistry)]
public class ProcessingRegistryModel : PageModel
{
    private readonly ProcessingActivityRegistryService _registryService;

    public ProcessingRegistryModel(ProcessingActivityRegistryService registryService)
    {
        _registryService = registryService;
    }

    [BindProperty(SupportsGet = true)]
    public int? Id { get; set; }

    public List<ProcessingActivityView> Activities { get; private set; } = new();

    public List<ProcessingActivityHistoryEntry> History { get; private set; } = new();

    public ProcessingActivityView? SelectedActivity { get; private set; }

    [BindProperty]
    public ProcessingActivityForm Input { get; set; } = new();

    [BindProperty]
    public ProcessingActivityForm EditInput { get; set; } = new();

    public List<string> Errors { get; private set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        if (!ModelState.IsValid)
        {
            await LoadAsync();
            return Page();
        }

        var actorId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var actorName = User.Identity?.Name ?? "System";
        var result = await _registryService.CreateAsync(
            new ProcessingActivityInput(
                null,
                Input.Name,
                Input.Purpose,
                Input.LegalBasis,
                Input.DataCategories,
                Input.DataSubjects,
                Input.Processors,
                Input.RetentionPeriod,
                Input.DataTransfers,
                Input.SecurityMeasures),
            actorId,
            actorName,
            HttpContext.RequestAborted);

        if (!result.Success)
        {
            Errors = result.Errors;
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            await LoadAsync();
            return Page();
        }

        StatusMessage = "Processing activity saved.";
        return RedirectToPage(new { id = result.Activity!.Id });
    }

    public async Task<IActionResult> OnPostUpdateAsync()
    {
        if (!ModelState.IsValid)
        {
            await LoadAsync();
            return Page();
        }

        var actorId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var actorName = User.Identity?.Name ?? "System";
        var result = await _registryService.UpdateAsync(
            new ProcessingActivityInput(
                EditInput.Id,
                EditInput.Name,
                EditInput.Purpose,
                EditInput.LegalBasis,
                EditInput.DataCategories,
                EditInput.DataSubjects,
                EditInput.Processors,
                EditInput.RetentionPeriod,
                EditInput.DataTransfers,
                EditInput.SecurityMeasures),
            actorId,
            actorName,
            HttpContext.RequestAborted);

        if (!result.Success)
        {
            Errors = result.Errors;
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            await LoadAsync();
            return Page();
        }

        StatusMessage = "Processing activity updated.";
        return RedirectToPage(new { id = result.Activity!.Id });
    }

    public async Task<IActionResult> OnGetExportAsync()
    {
        var content = await _registryService.ExportCsvAsync(HttpContext.RequestAborted);
        if (content.Length == 0)
        {
            StatusMessage = "No processing activities to export yet.";
            return RedirectToPage(new { id = Id });
        }

        var fileName = $"processing-registry-{DateTime.UtcNow:yyyyMMddHHmmss}.csv";
        return File(content, "text/csv", fileName);
    }

    private async Task LoadAsync()
    {
        Activities = await _registryService.GetAllAsync(HttpContext.RequestAborted);

        if (Id.HasValue)
        {
            SelectedActivity = await _registryService.FindAsync(Id.Value, HttpContext.RequestAborted);
            if (SelectedActivity != null)
            {
                EditInput = new ProcessingActivityForm
                {
                    Id = SelectedActivity.Id,
                    Name = SelectedActivity.Name,
                    Purpose = SelectedActivity.Purpose,
                    LegalBasis = SelectedActivity.LegalBasis,
                    DataCategories = SelectedActivity.DataCategories,
                    DataSubjects = SelectedActivity.DataSubjects,
                    Processors = SelectedActivity.Processors,
                    RetentionPeriod = SelectedActivity.RetentionPeriod,
                    DataTransfers = SelectedActivity.DataTransfers,
                    SecurityMeasures = SelectedActivity.SecurityMeasures
                };

                History = await _registryService.GetHistoryAsync(SelectedActivity.Id, HttpContext.RequestAborted);
            }
        }
    }

    public class ProcessingActivityForm
    {
        public int? Id { get; set; }

        [Required]
        [MaxLength(256)]
        [Display(Name = "Activity title")]
        public string Name { get; set; } = string.Empty;

        [Required]
        [MaxLength(1024)]
        [Display(Name = "Purpose of processing")]
        public string Purpose { get; set; } = string.Empty;

        [Required]
        [MaxLength(512)]
        [Display(Name = "Legal basis")]
        public string LegalBasis { get; set; } = string.Empty;

        [Required]
        [MaxLength(1024)]
        [Display(Name = "Categories of personal data")]
        public string DataCategories { get; set; } = string.Empty;

        [Required]
        [MaxLength(512)]
        [Display(Name = "Data subjects")]
        public string DataSubjects { get; set; } = string.Empty;

        [Required]
        [MaxLength(1024)]
        [Display(Name = "Processors / recipients")]
        public string Processors { get; set; } = string.Empty;

        [Required]
        [MaxLength(256)]
        [Display(Name = "Retention period")]
        public string RetentionPeriod { get; set; } = string.Empty;

        [MaxLength(512)]
        [Display(Name = "International transfers")]
        public string? DataTransfers { get; set; }

        [MaxLength(1024)]
        [Display(Name = "Security measures")]
        public string? SecurityMeasures { get; set; }
    }
}
