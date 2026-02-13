using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages.Admin.Settings
{
    [Authorize(Roles = AccountTypes.Admin)]
    public class CurrenciesModel : PageModel
    {
        private readonly CurrencyConfigurationService _currencyService;

        public CurrenciesModel(CurrencyConfigurationService currencyService)
        {
            _currencyService = currencyService;
        }

        [BindProperty]
        public List<CurrencyForm> Currencies { get; set; } = new();

        [BindProperty]
        public NewCurrencyForm NewCurrency { get; set; } = new();

        [BindProperty]
        [Required]
        public string BaseCurrency { get; set; } = string.Empty;

        [BindProperty]
        public bool ConfirmBaseCurrencyChange { get; set; }

        [TempData]
        public string? SuccessMessage { get; set; }

        public List<string> Errors { get; private set; } = new();

        public async Task OnGetAsync()
        {
            await LoadAsync();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                await LoadAsync();
                return Page();
            }

            var result = await _currencyService.SaveAsync(
                new CurrencySettingsInput(
                    Currencies.Select(c => new CurrencyUpdateInput(
                        c.Id,
                        c.Code ?? string.Empty,
                        c.Name,
                        c.EnabledForDisplay,
                        c.EnabledForTransactions)).ToList(),
                    BaseCurrency,
                    ConfirmBaseCurrencyChange,
                    string.IsNullOrWhiteSpace(NewCurrency?.Code)
                        ? null
                        : new NewCurrencyInput(
                            NewCurrency.Code!,
                            NewCurrency.Name,
                            NewCurrency.EnabledForDisplay,
                            NewCurrency.EnabledForTransactions)),
                HttpContext.RequestAborted);

            if (!result.Success)
            {
                Errors = result.Errors;
                foreach (var err in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, err);
                }

                await LoadAsync();
                return Page();
            }

            SuccessMessage = "Currency settings updated.";
            return RedirectToPage("/Admin/Settings/Currencies");
        }

        private async Task LoadAsync()
        {
            var currencies = await _currencyService.GetAllAsync(HttpContext.RequestAborted);
            Currencies = currencies
                .OrderByDescending(c => c.IsBase)
                .ThenBy(c => c.Code)
                .Select(c => new CurrencyForm
                {
                    Id = c.Id,
                    Code = c.Code,
                    Name = c.Name,
                    EnabledForDisplay = c.EnabledForDisplay,
                    EnabledForTransactions = c.EnabledForTransactions,
                    IsBase = c.IsBase,
                    LatestRate = c.LatestRate,
                    RateSource = c.RateSource,
                    RateUpdatedOn = c.RateUpdatedOn
                })
                .ToList();

            BaseCurrency = currencies.FirstOrDefault(c => c.IsBase)?.Code ?? BaseCurrency;
        }

        public class CurrencyForm
        {
            public int? Id { get; set; }

            [Required]
            [MaxLength(16)]
            public string? Code { get; set; }

            [MaxLength(128)]
            public string? Name { get; set; }

            public bool EnabledForDisplay { get; set; }

            public bool EnabledForTransactions { get; set; }

            public bool IsBase { get; set; }

            public decimal? LatestRate { get; set; }

            public string? RateSource { get; set; }

            public DateTimeOffset? RateUpdatedOn { get; set; }
        }

        public class NewCurrencyForm
        {
            [MaxLength(16)]
            public string? Code { get; set; }

            [MaxLength(128)]
            public string? Name { get; set; }

            public bool EnabledForDisplay { get; set; } = true;

            public bool EnabledForTransactions { get; set; }
        }
    }
}
