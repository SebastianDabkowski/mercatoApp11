using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages
{
    public class IndexModel : PageModel
    {
        private readonly ILogger<IndexModel> _logger;
        private readonly RecentlyViewedService _recentlyViewed;

        public IndexModel(ILogger<IndexModel> logger, RecentlyViewedService recentlyViewed)
        {
            _logger = logger;
            _recentlyViewed = recentlyViewed;
        }

        public IReadOnlyList<ProductModel> RecentlyViewedProducts { get; private set; } = Array.Empty<ProductModel>();

        public async Task OnGet()
        {
            RecentlyViewedProducts = await _recentlyViewed.GetProductsAsync(HttpContext);
        }
    }
}
