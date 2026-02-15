using System.ComponentModel.DataAnnotations;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages.Admin.Settings
{
    [Authorize(Policy = Permissions.AdminSettings)]
    public class IntegrationsModel : PageModel
    {
        private readonly IntegrationManagementService _integrationService;

        public IntegrationsModel(IntegrationManagementService integrationService)
        {
            _integrationService = integrationService;
        }

        public List<IntegrationView> Integrations { get; private set; } = new();

        [BindProperty]
        public IntegrationForm? Form { get; set; }

        [TempData]
        public string? SuccessMessage { get; set; }

        public List<string> Errors { get; private set; } = new();

        public async Task OnGetAsync(int? id = null)
        {
            await LoadAsync(id);
        }

        public async Task<IActionResult> OnPostSaveAsync()
        {
            if (Form == null)
            {
                await LoadAsync();
                return Page();
            }

            if (!ModelState.IsValid)
            {
                await LoadAsync(Form.Id);
                return Page();
            }

            var result = await _integrationService.SaveAsync(
                new IntegrationUpdateInput(
                    Form.Id,
                    Form.Key ?? string.Empty,
                    Form.Name ?? string.Empty,
                    Form.Type ?? IntegrationTypes.Payment,
                    Form.Environment ?? "Sandbox",
                    Form.Enabled,
                    Form.Endpoint,
                    Form.MerchantId,
                    Form.CallbackUrl,
                    string.IsNullOrWhiteSpace(Form.ApiKey) ? null : Form.ApiKey),
                HttpContext.RequestAborted);

            if (!result.Success)
            {
                Errors = result.Errors;
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error);
                }

                await LoadAsync(Form.Id);
                return Page();
            }

            SuccessMessage = "Integration saved.";
            return RedirectToPage("/Admin/Settings/Integrations", new { id = Form.Id });
        }

        public async Task<IActionResult> OnPostHealthCheckAsync()
        {
            if (Form?.Id == null)
            {
                await LoadAsync();
                return Page();
            }

            var result = await _integrationService.RunHealthCheckAsync(Form.Id.Value, HttpContext.RequestAborted);
            if (!result.Success)
            {
                ModelState.AddModelError(string.Empty, result.Message ?? "Health check failed.");
                Errors.Add(result.Message ?? "Health check failed.");
            }
            else
            {
                SuccessMessage = result.Message ?? "Health check completed.";
            }

            await LoadAsync(Form.Id);
            return Page();
        }

        private async Task LoadAsync(int? selectedId = null)
        {
            Errors = new List<string>();
            Integrations = await _integrationService.GetAllAsync(HttpContext.RequestAborted);
            var selected = selectedId.HasValue
                ? Integrations.FirstOrDefault(i => i.Id == selectedId.Value)
                : Integrations.FirstOrDefault();

            if (selected != null)
            {
                Form = new IntegrationForm
                {
                    Id = selected.Id,
                    Key = selected.Key,
                    Name = selected.Name,
                    Type = selected.Type,
                    Environment = selected.Environment,
                    Enabled = selected.Enabled,
                    Endpoint = selected.Endpoint,
                    MerchantId = selected.MerchantId,
                    CallbackUrl = selected.CallbackUrl,
                    ApiKeyPreview = selected.ApiKeyPreview,
                    Status = selected.Status,
                    LastHealthCheckMessage = selected.LastHealthCheckMessage,
                    LastHealthCheckOn = selected.LastHealthCheckOn
                };
            }
        }

        public class IntegrationForm
        {
            public int? Id { get; set; }

            [Required]
            [MaxLength(64)]
            public string? Key { get; set; }

            [Required]
            [MaxLength(128)]
            public string? Name { get; set; }

            [Required]
            [MaxLength(32)]
            public string? Type { get; set; }

            [Required]
            [MaxLength(32)]
            public string? Environment { get; set; }

            [MaxLength(256)]
            public string? ApiKey { get; set; }

            [MaxLength(256)]
            public string? Endpoint { get; set; }

            [MaxLength(128)]
            public string? MerchantId { get; set; }

            [MaxLength(256)]
            public string? CallbackUrl { get; set; }

            public bool Enabled { get; set; } = true;

            public string? ApiKeyPreview { get; set; }

            public string? Status { get; set; }

            public string? LastHealthCheckMessage { get; set; }

            public DateTimeOffset? LastHealthCheckOn { get; set; }
        }
    }
}
