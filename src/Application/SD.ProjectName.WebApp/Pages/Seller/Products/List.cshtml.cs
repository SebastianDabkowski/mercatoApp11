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
    public class ListModel : PageModel
    {
        private readonly GetProducts _getProducts;
        private readonly ArchiveProduct _archiveProduct;
        private readonly ChangeProductWorkflowState _changeWorkflowState;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<ListModel> _logger;
        private readonly ProductCatalogExportService _exportService;

        public ListModel(GetProducts getProducts, ArchiveProduct archiveProduct, ChangeProductWorkflowState changeWorkflowState, UserManager<ApplicationUser> userManager, ILogger<ListModel> logger, ProductCatalogExportService exportService)
        {
            _getProducts = getProducts;
            _archiveProduct = archiveProduct;
            _changeWorkflowState = changeWorkflowState;
            _userManager = userManager;
            _logger = logger;
            _exportService = exportService;
        }

        public List<ProductModel> Products { get; private set; } = new();

        [BindProperty(SupportsGet = true)]
        public string? Search { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? WorkflowStateFilter { get; set; }

        [BindProperty]
        public string ExportFormat { get; set; } = "csv";

        [BindProperty]
        public bool ExportFilteredOnly { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            var normalizedState = NormalizeWorkflowState(WorkflowStateFilter);
            WorkflowStateFilter = normalizedState;
            Products = await _getProducts.GetFilteredList(user.Id, includeDrafts: true, Search, normalizedState);
            return Page();
        }

        public async Task<IActionResult> OnPostActivateAsync(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            var product = await _getProducts.GetById(id, includeDrafts: true);
            if (product == null)
            {
                return NotFound();
            }

            if (product.SellerId != user.Id)
            {
                return Forbid();
            }

            var result = await _changeWorkflowState.SetStateAsync(product, ProductWorkflowStates.Active);
            if (!result.Succeeded)
            {
                TempData["ErrorMessage"] = string.Join(" ", result.Errors);
                return RedirectToPage();
            }

            TempData["StatusMessage"] = "Product published.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostSuspendAsync(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            var product = await _getProducts.GetById(id, includeDrafts: true);
            if (product == null)
            {
                return NotFound();
            }

            if (product.SellerId != user.Id)
            {
                return Forbid();
            }

            var result = await _changeWorkflowState.SetStateAsync(product, ProductWorkflowStates.Suspended);
            if (!result.Succeeded)
            {
                TempData["ErrorMessage"] = string.Join(" ", result.Errors);
                return RedirectToPage();
            }

            TempData["StatusMessage"] = "Product suspended.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDeleteAsync(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            var product = await _getProducts.GetById(id, includeDrafts: true);
            if (product == null)
            {
                return NotFound();
            }

            if (product.SellerId != user.Id)
            {
                return Forbid();
            }

            await _archiveProduct.ArchiveAsync(product);
            _logger.LogInformation("Product {ProductId} archived by seller {SellerId}", id, user.Id);

            TempData["StatusMessage"] = "Product deleted.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostExportAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            var normalizedState = NormalizeWorkflowState(WorkflowStateFilter);
            var options = new ProductExportOptions
            {
                Format = ExportFormat,
                UseFilters = ExportFilteredOnly,
                Search = ExportFilteredOnly ? Search : null,
                WorkflowState = ExportFilteredOnly ? normalizedState : null
            };

            await _exportService.QueueAsync(user.Id, options);
            TempData["StatusMessage"] = "Export requested. Check export history to download once ready.";
            return RedirectToPage(new { search = Search, workflowState = WorkflowStateFilter });
        }

        private static string? NormalizeWorkflowState(string? state)
        {
            if (string.IsNullOrWhiteSpace(state))
            {
                return null;
            }

            var normalized = state.Trim().ToLowerInvariant();
            return ProductWorkflowStates.IsValid(normalized) && normalized != ProductWorkflowStates.Archived
                ? normalized
                : null;
        }
    }
}
