using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using SD.ProjectName.Modules.Products.Application;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.WebApp.Identity;

namespace SD.ProjectName.WebApp.Pages.Seller.Products
{
    [Authorize(Roles = AccountTypes.Seller)]
    public class BulkUpdateModel : PageModel
    {
        private readonly GetProducts _getProducts;
        private readonly BulkUpdateProducts _bulkUpdater;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<BulkUpdateModel> _logger;

        public BulkUpdateModel(GetProducts getProducts, BulkUpdateProducts bulkUpdater, UserManager<ApplicationUser> userManager, ILogger<BulkUpdateModel> logger)
        {
            _getProducts = getProducts;
            _bulkUpdater = bulkUpdater;
            _userManager = userManager;
            _logger = logger;
        }

        [BindProperty(SupportsGet = true)]
        public List<int> SelectedIds { get; set; } = new();

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public List<ProductModel> SelectedProducts { get; private set; } = new();

        public List<BulkUpdateItemResult> PreviewResults { get; private set; } = new();

        public List<string> ValidationMessages { get; private set; } = new();

        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            if (!SelectedIds.Any())
            {
                TempData["ErrorMessage"] = "Select at least one product to update.";
                return RedirectToPage("List");
            }

            Input.SelectedIds = SelectedIds;
            await LoadSelectedProducts(user.Id);
            return Page();
        }

        public async Task<IActionResult> OnPostPreviewAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            SelectedIds = Input.SelectedIds ?? new List<int>();
            Input.SelectedIds = SelectedIds;
            await LoadSelectedProducts(user.Id);
            if (!SelectedProducts.Any())
            {
                TempData["ErrorMessage"] = "Select at least one product to update.";
                return RedirectToPage("List");
            }

            var command = ToCommand(Input);
            var result = await _bulkUpdater.PreviewAsync(user.Id, SelectedIds, command);

            PreviewResults = result.Items;
            ValidationMessages = result.ValidationErrors;
            foreach (var error in result.ValidationErrors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            return Page();
        }

        public async Task<IActionResult> OnPostApplyAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            SelectedIds = Input.SelectedIds ?? new List<int>();
            Input.SelectedIds = SelectedIds;
            await LoadSelectedProducts(user.Id);
            if (!SelectedProducts.Any())
            {
                TempData["ErrorMessage"] = "Select at least one product to update.";
                return RedirectToPage("List");
            }

            var command = ToCommand(Input);
            var result = await _bulkUpdater.ApplyAsync(user.Id, SelectedIds, command);

            PreviewResults = result.Items;
            ValidationMessages = result.ValidationErrors;
            foreach (var error in result.ValidationErrors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            if (result.ValidationErrors.Any())
            {
                return Page();
            }

            var applied = result.Items.Count(i => i.Applied);
            var failures = result.Items.Count(i => !string.IsNullOrWhiteSpace(i.Error));

            if (applied == 0)
            {
                ModelState.AddModelError(string.Empty, "No products were updated. Review the errors below.");
                return Page();
            }

            _logger.LogInformation("Bulk update applied by seller {SellerId} to {AppliedCount} products. Price op: {PriceOp} ({PriceValue}), Stock op: {StockOp} ({StockValue})",
                user.Id,
                applied,
                command.PriceOperation,
                command.PriceValue,
                command.StockOperation,
                command.StockValue);

            TempData["StatusMessage"] = $"Updated {applied} product{(applied == 1 ? string.Empty : "s")}.";
            if (failures > 0)
            {
                var failedDetails = string.Join(", ", result.Items.Where(i => !string.IsNullOrWhiteSpace(i.Error)).Select(i => $"{i.Title}: {i.Error}"));
                TempData["ErrorMessage"] = $"Failed to update {failures} product{(failures == 1 ? string.Empty : "s")}: {failedDetails}";
            }

            return RedirectToPage("List");
        }

        private async Task LoadSelectedProducts(string sellerId)
        {
            var all = await _getProducts.GetList(sellerId, includeDrafts: true);
            SelectedProducts = all
                .Where(p => SelectedIds.Contains(p.Id))
                .ToList();
        }

        private static BulkUpdateCommand ToCommand(InputModel input)
        {
            return new BulkUpdateCommand
            {
                PriceOperation = input.PriceOperation,
                PriceValue = input.PriceValue,
                StockOperation = input.StockOperation,
                StockValue = input.StockValue
            };
        }

        public class InputModel
        {
            [BindProperty]
            public List<int> SelectedIds { get; set; } = new();

            [Display(Name = "Price change")]
            public BulkPriceOperation PriceOperation { get; set; }

            [Range(0, double.MaxValue)]
            [Display(Name = "Price value")]
            public decimal? PriceValue { get; set; }

            [Display(Name = "Stock change")]
            public BulkStockOperation StockOperation { get; set; }

            [Range(0, int.MaxValue)]
            [Display(Name = "Stock value")]
            public int? StockValue { get; set; }
        }
    }
}
