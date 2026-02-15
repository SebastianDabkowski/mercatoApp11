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
    public class CommissionsModel : PageModel
    {
        private readonly CommissionRuleService _commissionRules;
        private readonly CartOptions _cartOptions;

        public CommissionsModel(CommissionRuleService commissionRules, CartOptions cartOptions)
        {
            _commissionRules = commissionRules;
            _cartOptions = cartOptions;
        }

        [BindProperty]
        public RuleForm Input { get; set; } = new();

        public List<CommissionRule> Rules { get; private set; } = new();

        public Dictionary<string, decimal> CategoryRates { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

        public decimal DefaultRate => _cartOptions.PlatformCommissionRate;

        public decimal DefaultFixedFee => _cartOptions.PlatformFixedFee;

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

            var result = await _commissionRules.SaveAsync(
                new CommissionRuleInput
                {
                    Id = Input.Id,
                    Name = Input.Name?.Trim() ?? string.Empty,
                    Rate = Input.Rate,
                    FixedFee = Input.FixedFee,
                    Category = Input.Category,
                    SellerType = Input.SellerType,
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

            SuccessMessage = Input.Id.HasValue ? "Commission rule updated." : "Commission rule created.";
            return RedirectToPage("/Admin/Settings/Commissions");
        }

        private async Task LoadAsync(int? id)
        {
            Rules = await _commissionRules.GetAllAsync(HttpContext.RequestAborted);
            CategoryRates = _cartOptions.CategoryCommissionRates ?? new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

            if (id.HasValue)
            {
                var match = await _commissionRules.FindAsync(id.Value, HttpContext.RequestAborted);
                if (match != null)
                {
                    Input = new RuleForm
                    {
                        Id = match.Id,
                        Name = match.Name,
                        Rate = match.Rate,
                        FixedFee = match.FixedFee,
                        Category = match.Category,
                        SellerType = match.SellerType,
                        EffectiveFrom = match.EffectiveFrom.UtcDateTime,
                        EffectiveTo = match.EffectiveTo?.UtcDateTime
                    };
                }
            }
        }

        public class RuleForm
        {
            public int? Id { get; set; }

            [Required]
            [MaxLength(256)]
            public string Name { get; set; } = string.Empty;

            [Required]
            [Range(0, 1)]
            public decimal Rate { get; set; }

            [Range(0, double.MaxValue)]
            public decimal FixedFee { get; set; }

            [MaxLength(256)]
            public string? Category { get; set; }

            [MaxLength(32)]
            public string? SellerType { get; set; }

            [Required]
            [DataType(DataType.Date)]
            public DateTime EffectiveFrom { get; set; } = DateTime.UtcNow.Date;

            [DataType(DataType.Date)]
            public DateTime? EffectiveTo { get; set; }
        }
    }
}
