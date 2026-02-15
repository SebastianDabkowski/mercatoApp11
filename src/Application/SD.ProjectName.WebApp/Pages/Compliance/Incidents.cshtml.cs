using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages.Compliance;

[Authorize(Policy = Permissions.ComplianceRegistry)]
public class IncidentsModel : PageModel
{
    private const int DefaultPageSize = 25;
    private readonly SecurityIncidentService _incidentService;
    private readonly SecurityIncidentOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly UserManager<ApplicationUser> _userManager;

    public IncidentsModel(
        SecurityIncidentService incidentService,
        SecurityIncidentOptions options,
        TimeProvider timeProvider,
        UserManager<ApplicationUser> userManager)
    {
        _incidentService = incidentService;
        _options = options;
        _timeProvider = timeProvider;
        _userManager = userManager;
    }

    [BindProperty(SupportsGet = true)]
    [DataType(DataType.Date)]
    public DateTime? FromDate { get; set; }

    [BindProperty(SupportsGet = true)]
    [DataType(DataType.Date)]
    public DateTime? ToDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Severity { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Status { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Source { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Rule { get; set; }

    [BindProperty(SupportsGet = true, Name = "page")]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int? Id { get; set; }

    [BindProperty]
    public StatusUpdateForm StatusInput { get; set; } = new();

    public PagedResult<SecurityIncidentView> Incidents { get; private set; } = new()
    {
        Items = new List<SecurityIncidentView>(),
        PageNumber = 1,
        PageSize = DefaultPageSize,
        TotalCount = 0
    };

    public SecurityIncidentView? SelectedIncident { get; private set; }

    public List<SecurityIncidentStatusEntry> History { get; private set; } = new();

    public IReadOnlyCollection<string> Statuses => SecurityIncidentStatuses.All;

    public IReadOnlyCollection<string> SeverityLevels => _options.SeverityOrder;

    public List<string> Errors { get; private set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostUpdateStatusAsync()
    {
        if (!ModelState.IsValid)
        {
            await LoadAsync();
            return Page();
        }

        var actor = await _userManager.GetUserAsync(User);
        var actorId = actor?.Id ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var actorName = actor?.FullName ?? User.Identity?.Name ?? "System";
        var result = await _incidentService.UpdateStatusAsync(
            new SecurityIncidentStatusUpdate(
                StatusInput.IncidentId,
                StatusInput.Status,
                actorId,
                actorName,
                StatusInput.Notes),
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

        StatusMessage = "Incident status updated.";
        return RedirectToPage(new
        {
            id = StatusInput.IncidentId,
            page = PageNumber,
            severity = Severity,
            status = Status,
            source = Source,
            rule = Rule,
            fromDate = FromDate?.ToString("yyyy-MM-dd"),
            toDate = ToDate?.ToString("yyyy-MM-dd")
        });
    }

    public async Task<IActionResult> OnGetExportAsync()
    {
        var from = NormalizeStartOfDay(FromDate) ?? _timeProvider.GetUtcNow().AddDays(-30);
        var to = NormalizeEndOfDay(ToDate) ?? _timeProvider.GetUtcNow();
        var export = await _incidentService.ExportAsync(from, to, HttpContext.RequestAborted);
        if (export.RowCount == 0)
        {
            StatusMessage = "No incidents found for the selected window.";
            return RedirectToPage(new
            {
                page = PageNumber,
                severity = Severity,
                status = Status,
                source = Source,
                rule = Rule,
                fromDate = FromDate?.ToString("yyyy-MM-dd"),
                toDate = ToDate?.ToString("yyyy-MM-dd")
            });
        }

        var fileName = $"security-incidents-{from:yyyyMMddHHmmss}-{to:yyyyMMddHHmmss}.csv";
        return File(export.Content, "text/csv", fileName);
    }

    private async Task LoadAsync()
    {
        PageNumber = Math.Max(1, PageNumber);
        var filters = new SecurityIncidentFilters
        {
            From = NormalizeStartOfDay(FromDate),
            To = NormalizeEndOfDay(ToDate),
            Severity = string.IsNullOrWhiteSpace(Severity) ? null : Severity,
            Status = string.IsNullOrWhiteSpace(Status) ? null : Status,
            Source = string.IsNullOrWhiteSpace(Source) ? null : Source,
            Rule = string.IsNullOrWhiteSpace(Rule) ? null : Rule
        };

        Incidents = await _incidentService.GetAsync(filters, PageNumber, DefaultPageSize, HttpContext.RequestAborted);

        if (Id.HasValue)
        {
            SelectedIncident = await _incidentService.GetAsync(Id.Value, HttpContext.RequestAborted);
            if (SelectedIncident != null)
            {
                StatusInput = new StatusUpdateForm
                {
                    IncidentId = SelectedIncident.Id,
                    Status = SelectedIncident.Status,
                    Notes = SelectedIncident.ResolutionNotes
                };

                History = await _incidentService.GetHistoryAsync(SelectedIncident.Id, HttpContext.RequestAborted);
            }
        }
    }

    private static DateTimeOffset? NormalizeStartOfDay(DateTime? date)
    {
        if (!date.HasValue)
        {
            return null;
        }

        var unspecified = DateTime.SpecifyKind(date.Value, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, TimeSpan.Zero);
    }

    private static DateTimeOffset? NormalizeEndOfDay(DateTime? date)
    {
        if (!date.HasValue)
        {
            return null;
        }

        var unspecified = DateTime.SpecifyKind(date.Value, DateTimeKind.Unspecified)
            .AddDays(1)
            .AddTicks(-1);
        return new DateTimeOffset(unspecified, TimeSpan.Zero);
    }

    public class StatusUpdateForm
    {
        [Required]
        public int IncidentId { get; set; }

        [Required]
        [MaxLength(32)]
        public string Status { get; set; } = SecurityIncidentStatuses.New;

        [MaxLength(1024)]
        [Display(Name = "Resolution notes (optional)")]
        public string? Notes { get; set; }
    }
}
