using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.Modules.Products.Application;
using SD.ProjectName.Modules.Products.Domain;

namespace SD.ProjectName.WebApp.Pages.Products
{
    public class ListModel : PageModel
    {
        private readonly ILogger<ListModel> _logger;
        private readonly GetProducts _getProducts;

        public ListModel(ILogger<ListModel> logger, GetProducts getProducts)
        {
            _logger = logger;
            _getProducts = getProducts;
        }

        public List<ProductModel> Products { get; set; } = new();

        public async Task OnGetAsync()
        {
            Products = await _getProducts.GetList();
        }
    }
}
