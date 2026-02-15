using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.Modules.Products.Application;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.WebApp.Identity;

namespace SD.ProjectName.WebApp.Pages.Seller.Products
{
    [Authorize(Policy = Permissions.SellerWorkspace)]
    public class ExportHistoryModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ProductCatalogExportService _exportService;

        public ExportHistoryModel(UserManager<ApplicationUser> userManager, ProductCatalogExportService exportService)
        {
            _userManager = userManager;
            _exportService = exportService;
        }

        public List<ProductExportJob> Jobs { get; private set; } = new();

        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            Jobs = await _exportService.GetHistoryAsync(user.GetSellerTenantId(), take: 100);
            return Page();
        }

        public async Task<IActionResult> OnGetDownloadAsync(Guid id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            var job = await _exportService.GetJobAsync(id, user.GetSellerTenantId());
            if (job == null || job.FileContent == null || job.Status != ProductExportJobStatus.Completed)
            {
                return NotFound();
            }

            var fileName = string.IsNullOrWhiteSpace(job.FileName) ? $"products-export.{job.Format}" : job.FileName;
            return File(job.FileContent, job.ContentType ?? "text/csv", fileName);
        }
    }
}
