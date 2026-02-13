using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.Modules.Products.Application;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.WebApp.Identity;

namespace SD.ProjectName.WebApp.Pages.Seller.Products
{
    [Authorize(Roles = AccountTypes.Seller)]
    public class ListModel : PageModel
    {
        private readonly GetProducts _getProducts;
        private readonly UserManager<ApplicationUser> _userManager;

        public ListModel(GetProducts getProducts, UserManager<ApplicationUser> userManager)
        {
            _getProducts = getProducts;
            _userManager = userManager;
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
    }
}
