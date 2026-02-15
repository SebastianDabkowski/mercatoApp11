using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages.Admin.Settings
{
    [Authorize(Policy = Permissions.AdminSettings)]
    public class VatModel : PageModel
    {
        private readonly VatRuleService _vatRules;

        public VatModel(VatRuleService vatRules)
        {
            _vatRules = vatRules;
        }

        [BindProperty]
        public RuleForm Input { get; set; } = new();

        public List<VatRule> Rules { get; private set; } = new();

        public List<VatRuleAuditView> History { get; private set; } = new();

        public List<string> Errors { get; private set; } = new();

        [TempData]
        public string? SuccessMessage { get; set; }

        public async Task OnGetAsync(int? id = null)
        {
            await LoadAsync(id);
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                await LoadAsync(Input.Id);
                return Page();
            }

            var actorId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            var actorName = User.Identity?.Name ?? "Admin";
            var categories = ParseCategories(Input.Categories);

            var result = await _vatRules.SaveAsync(
                new VatRuleInput
                {
                    Id = Input.Id,
                    Country = Input.Country?.Trim() ?? string.Empty,
                    Rate = Input.Rate,
                    Categories = categories,
                    EffectiveFrom = DateTime.SpecifyKind(Input.EffectiveFrom, DateTimeKind.Utc),
                    EffectiveTo = Input.EffectiveTo.HasValue ? DateTime.SpecifyKind(Input.EffectiveTo.Value, DateTimeKind.Utc) : null
                },
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

                await LoadAsync(Input.Id);
                return Page();
            }

            SuccessMessage = Input.Id.HasValue ? "VAT rule updated." : "VAT rule created.";
            return RedirectToPage("/Admin/Settings/Vat");
        }

        private async Task LoadAsync(int? id)
        {
            Rules = await _vatRules.GetAllAsync(HttpContext.RequestAborted);
            History = await BuildHistoryAsync();

            if (id.HasValue)
            {
                var match = await _vatRules.FindAsync(id.Value, HttpContext.RequestAborted);
                if (match != null)
                {
                    Input = new RuleForm
                    {
                        Id = match.Id,
                        Country = match.Country,
                        Rate = match.Rate,
                        Categories = match.Categories,
                        EffectiveFrom = match.EffectiveFrom.UtcDateTime,
                        EffectiveTo = match.EffectiveTo?.UtcDateTime
                    };
                }
            }
        }

        private async Task<List<VatRuleAuditView>> BuildHistoryAsync()
        {
            var audits = await _vatRules.GetHistoryAsync(null, HttpContext.RequestAborted);
            var history = new List<VatRuleAuditView>();
            foreach (var audit in audits)
            {
                VatRule? snapshot = null;
                try
                {
                    snapshot = JsonSerializer.Deserialize<VatRule>(audit.SnapshotJson);
                }
                catch
                {
                    snapshot = null;
                }

                history.Add(new VatRuleAuditView(
                    audit.Id,
                    audit.RuleId,
                    snapshot?.Country ?? string.Empty,
                    snapshot?.Rate ?? 0,
                    ParseCategories(snapshot?.Categories),
                    snapshot?.EffectiveFrom ?? audit.ChangedOn,
                    snapshot?.EffectiveTo,
                    audit.Action,
                    audit.ChangedByName ?? audit.ChangedBy ?? "Unknown",
                    audit.ChangedOn));
            }

            return history;
        }

        private static List<string> ParseCategories(string? categories)
        {
            if (string.IsNullOrWhiteSpace(categories))
            {
                return new List<string>();
            }

            return categories
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(c => c.Trim())
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public class RuleForm
        {
            public int? Id { get; set; }

            [Required]
            [MaxLength(120)]
            public string Country { get; set; } = string.Empty;

            [Required]
            [Range(0, 1)]
            public decimal Rate { get; set; }

            [MaxLength(512)]
            public string? Categories { get; set; }

            [Required]
            [DataType(DataType.Date)]
            public DateTime EffectiveFrom { get; set; } = DateTime.UtcNow.Date;

            [DataType(DataType.Date)]
            public DateTime? EffectiveTo { get; set; }
        }

        public record VatRuleAuditView(
            int Id,
            int RuleId,
            string Country,
            decimal Rate,
            List<string> Categories,
            DateTimeOffset EffectiveFrom,
            DateTimeOffset? EffectiveTo,
            string Action,
            string ChangedBy,
            DateTimeOffset ChangedOn);
    }
}
