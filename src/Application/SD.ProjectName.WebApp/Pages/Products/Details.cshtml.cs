using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using SD.ProjectName.Modules.Products.Application;
using SD.ProjectName.Modules.Products.Domain;

namespace SD.ProjectName.WebApp.Pages.Products
{
    public class DetailsModel : PageModel
    {
        private readonly GetProducts _getProducts;
        private readonly ILogger<DetailsModel> _logger;

        public DetailsModel(GetProducts getProducts, ILogger<DetailsModel> logger)
        {
            _getProducts = getProducts;
            _logger = logger;
        }

        public ProductModel? Product { get; private set; }

        [TempData]
        public string? StatusMessage { get; set; }

        public async Task<IActionResult> OnGetAsync(int id)
        {
            Product = await _getProducts.GetById(id);
            if (Product == null)
            {
                Response.StatusCode = StatusCodes.Status404NotFound;
                StatusMessage = "This product is unavailable.";
                _logger.LogInformation("Product {ProductId} unavailable or archived when requested.", id);
                return Page();
            }

            return Page();
        }
    }
}
