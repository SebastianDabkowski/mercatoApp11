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
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<ListModel> _logger;

        public ListModel(GetProducts getProducts, ArchiveProduct archiveProduct, UserManager<ApplicationUser> userManager, ILogger<ListModel> logger)
        {
            _getProducts = getProducts;
            _archiveProduct = archiveProduct;
            _userManager = userManager;
            _logger = logger;
        }

        public List<ProductModel> Products { get; private set; } = new();

        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            Products = await _getProducts.GetList(user.Id, includeDrafts: true);
            return Page();
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
    }
}
