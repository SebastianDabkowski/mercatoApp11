using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages.Admin.Settings;

[Authorize(Policy = Permissions.AdminSettings)]
public class FeatureFlagsModel : PageModel
{
    private readonly FeatureFlagService _featureFlagService;

    public FeatureFlagsModel(FeatureFlagService featureFlagService)
    {
        _featureFlagService = featureFlagService;
    }

    public List<FeatureFlagView> Flags { get; private set; } = new();

    [BindProperty]
    public FlagForm Input { get; set; } = new();

    public List<string> Errors { get; private set; } = new();

    [TempData]
    public string? SuccessMessage { get; set; }

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

        var envInputs = Input.Environments?.Select(e => new FeatureFlagEnvironmentInput(
            e.Environment ?? string.Empty,
            e.Enabled,
            new FeatureFlagTargetingInput(
                e.InternalOnly,
                Split(e.AllowedUsers),
                Split(e.AllowedSellers),
                e.PercentageRollout)))
            .ToList() ?? new List<FeatureFlagEnvironmentInput>();

        var actorId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var actorName = User.Identity?.Name ?? "Admin";

        var result = await _featureFlagService.SaveAsync(
            new FeatureFlagInput(
                null,
                Input.Key,
                Input.Name,
                Input.Description,
                Input.DefaultEnabled,
                envInputs),
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

        SuccessMessage = "Feature flag created.";
        return RedirectToPage("/Admin/Settings/FeatureFlags");
    }

    public async Task<IActionResult> OnPostToggleAsync(int flagId, string environment, bool enabled)
    {
        var actorId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var actorName = User.Identity?.Name ?? "Admin";

        var result = await _featureFlagService.SetEnvironmentStateAsync(
            flagId,
            environment,
            enabled,
            actorId,
            actorName,
            HttpContext.RequestAborted);

        if (!result.Success)
        {
            Errors = result.Errors;
            ModelState.AddModelError(string.Empty, string.Join("; ", result.Errors));
        }
        else
        {
            SuccessMessage = $"Flag {environment} environment {(enabled ? "enabled" : "disabled")}.";
        }

        await LoadAsync();
        return Page();
    }

    private async Task LoadAsync()
    {
        Flags = await _featureFlagService.GetAllAsync(HttpContext.RequestAborted);
        if (Input.Environments == null || Input.Environments.Count == 0)
        {
            Input.Environments = DefaultEnvironments();
        }
    }

    private static List<EnvironmentForm> DefaultEnvironments() =>
        new()
        {
            new EnvironmentForm { Environment = "Development", Enabled = true },
            new EnvironmentForm { Environment = "Test", Enabled = false },
            new EnvironmentForm { Environment = "Staging", Enabled = false },
            new EnvironmentForm { Environment = "Production", Enabled = false }
        };

    private static List<string> Split(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return new List<string>();
        }

        return csv.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(v => v.Trim())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public class FlagForm
    {
        [Required]
        [MaxLength(128)]
        public string Key { get; set; } = string.Empty;

        [Required]
        [MaxLength(256)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(512)]
        public string? Description { get; set; }

        public bool DefaultEnabled { get; set; }

        public List<EnvironmentForm> Environments { get; set; } = new();
    }

    public class EnvironmentForm
    {
        [Required]
        [MaxLength(32)]
        public string Environment { get; set; } = string.Empty;

        public bool Enabled { get; set; }

        [Range(0, 100)]
        public int? PercentageRollout { get; set; }

        public bool InternalOnly { get; set; }

        [Display(Name = "Allow users (comma separated)")]
        public string? AllowedUsers { get; set; }

        [Display(Name = "Allow sellers (comma separated)")]
        public string? AllowedSellers { get; set; }
    }
}
