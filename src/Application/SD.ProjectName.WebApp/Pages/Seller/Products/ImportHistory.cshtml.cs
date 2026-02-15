using System.Text;
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
    public class ImportHistoryModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ProductCatalogImportService _importService;

        public ImportHistoryModel(UserManager<ApplicationUser> userManager, ProductCatalogImportService importService)
        {
            _userManager = userManager;
            _importService = importService;
        }

        public List<ProductImportJob> Jobs { get; private set; } = new();

        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            Jobs = await _importService.GetHistoryAsync(user.Id, take: 100);
            return Page();
        }

        public async Task<IActionResult> OnGetDownloadAsync(Guid id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            var job = await _importService.GetJobAsync(id, user.Id);
            if (job == null || string.IsNullOrWhiteSpace(job.ErrorReport))
            {
                return NotFound();
            }

            var fileName = $"{Path.GetFileNameWithoutExtension(job.FileName)}-errors.txt";
            var bytes = Encoding.UTF8.GetBytes(job.ErrorReport);
            return File(bytes, "text/plain", fileName);
        }
    }
}
