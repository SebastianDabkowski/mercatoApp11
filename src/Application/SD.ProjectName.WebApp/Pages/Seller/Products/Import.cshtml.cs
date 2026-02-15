using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using SD.ProjectName.Modules.Products.Application;
using SD.ProjectName.WebApp.Identity;

namespace SD.ProjectName.WebApp.Pages.Seller.Products
{
    [Authorize(Policy = Permissions.SellerWorkspace)]
    public class ImportModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ProductCatalogImportService _importService;
        private readonly ILogger<ImportModel> _logger;

        public ImportModel(UserManager<ApplicationUser> userManager, ProductCatalogImportService importService, ILogger<ImportModel> logger)
        {
            _userManager = userManager;
            _importService = importService;
            _logger = logger;
        }

        [BindProperty]
        [Required(ErrorMessage = "Select a CSV or XLS file.")]
        [Display(Name = "Product file (CSV or XLS/XLSX)")]
        public IFormFile? Upload { get; set; }

        public ProductImportPreview? Preview { get; private set; }

        public Guid? PendingJobId { get; private set; }

        public async Task<IActionResult> OnPostPreviewAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            var sellerId = user.GetSellerTenantId();
            if (Upload == null || Upload.Length == 0)
            {
                ModelState.AddModelError(nameof(Upload), "Upload a CSV or XLS file.");
                return Page();
            }

            await using var ms = new MemoryStream();
            await Upload.CopyToAsync(ms);
            var bytes = ms.ToArray();

            var (preview, job) = await _importService.CreatePendingJobAsync(sellerId, bytes, Upload.FileName);
            Preview = preview;

            if (job != null)
            {
                PendingJobId = job.Id;
                TempData["StatusMessage"] = "File validated. Review the summary and start the import.";
            }
            else
            {
                TempData["ErrorMessage"] = "Fix the listed issues before importing.";
            }

            return Page();
        }

        public async Task<IActionResult> OnPostConfirmAsync(Guid jobId)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            var queued = await _importService.QueueAsync(jobId, user.GetSellerTenantId());
            if (!queued)
            {
                TempData["ErrorMessage"] = "Unable to start import. Validate the file again.";
                return RedirectToPage();
            }

            TempData["StatusMessage"] = "Import started. You can track progress in import history.";
            return RedirectToPage("ImportHistory");
        }

        public IActionResult OnGetTemplate()
        {
            const string header = "SKU,Title,Description,Price,Stock,Category,ShippingMethods,MainImageUrl,GalleryImageUrls,WeightKg,LengthCm,WidthCm,HeightCm";
            var content = header + Environment.NewLine;
            var bytes = Encoding.UTF8.GetBytes(content);
            return File(bytes, "text/csv", "product-import-template.csv");
        }
    }
}
