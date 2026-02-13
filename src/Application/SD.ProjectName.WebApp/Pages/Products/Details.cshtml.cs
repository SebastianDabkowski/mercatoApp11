using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using SD.ProjectName.Modules.Products.Application;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages.Products
{
    public class DetailsModel : PageModel
    {
        private readonly GetProducts _getProducts;
        private readonly RecentlyViewedService _recentlyViewed;
        private readonly ILogger<DetailsModel> _logger;

        public DetailsModel(GetProducts getProducts, RecentlyViewedService recentlyViewed, ILogger<DetailsModel> logger)
        {
            _getProducts = getProducts;
            _recentlyViewed = recentlyViewed;
            _logger = logger;
        }

        public ProductModel? Product { get; private set; }
        public IReadOnlyList<ProductModel> RecentlyViewedProducts { get; private set; } = Array.Empty<ProductModel>();

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
                RecentlyViewedProducts = await _recentlyViewed.GetProductsAsync(HttpContext, id);
                return Page();
            }

            _recentlyViewed.RememberProduct(HttpContext, Product.Id);
            RecentlyViewedProducts = await _recentlyViewed.GetProductsAsync(HttpContext, Product.Id);

            return Page();
        }
    }
}
