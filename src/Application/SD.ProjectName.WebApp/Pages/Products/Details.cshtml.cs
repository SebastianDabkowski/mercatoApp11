using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using SD.ProjectName.Modules.Products.Application;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages.Products
{
    public class DetailsModel : PageModel
    {
        private readonly GetProducts _getProducts;
        private readonly RecentlyViewedService _recentlyViewed;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<DetailsModel> _logger;

        public DetailsModel(GetProducts getProducts, RecentlyViewedService recentlyViewed, UserManager<ApplicationUser> userManager, ILogger<DetailsModel> logger)
        {
            _getProducts = getProducts;
            _recentlyViewed = recentlyViewed;
            _userManager = userManager;
            _logger = logger;
        }

        public ProductModel? Product { get; private set; }
        public IReadOnlyList<ProductModel> RecentlyViewedProducts { get; private set; } = Array.Empty<ProductModel>();
        public string? ReturnUrl { get; private set; }
        public string? SellerDisplayName { get; private set; }
        public string? SellerSlug { get; private set; }
        public bool HasSellerLink => !string.IsNullOrWhiteSpace(SellerSlug) && !string.IsNullOrWhiteSpace(SellerDisplayName);

        [TempData]
        public string? StatusMessage { get; set; }

        public async Task<IActionResult> OnGetAsync(int id, string? returnUrl = null)
        {
            ReturnUrl = GetSafeReturnUrl(returnUrl);
            Product = await _getProducts.GetById(id);
            if (Product == null)
            {
                Response.StatusCode = StatusCodes.Status404NotFound;
                StatusMessage = "This product is unavailable.";
                _logger.LogInformation("Product {ProductId} unavailable or archived when requested.", id);
                RecentlyViewedProducts = await _recentlyViewed.GetProductsAsync(HttpContext, id);
                return Page();
            }

            await LoadSellerMetadata(Product.SellerId);
            _recentlyViewed.RememberProduct(HttpContext, Product.Id);
            RecentlyViewedProducts = await _recentlyViewed.GetProductsAsync(HttpContext, Product.Id);

            return Page();
        }

        private string? GetSafeReturnUrl(string? returnUrl)
        {
            if (string.IsNullOrWhiteSpace(returnUrl))
            {
                return null;
            }

            return Url.IsLocalUrl(returnUrl) ? returnUrl : null;
        }

        private async Task LoadSellerMetadata(string sellerId)
        {
            if (string.IsNullOrWhiteSpace(sellerId))
            {
                return;
            }

            var seller = await _userManager.FindByIdAsync(sellerId);
            if (seller == null || string.IsNullOrWhiteSpace(seller.BusinessName))
            {
                return;
            }

            var slug = NormalizeSlug(seller.BusinessName);
            if (string.IsNullOrWhiteSpace(slug))
            {
                return;
            }

            SellerDisplayName = seller.BusinessName;
            SellerSlug = slug;
        }

        private static string NormalizeSlug(string value)
        {
            var trimmed = value.Trim().ToLowerInvariant();
            var builder = new StringBuilder();
            var lastWasHyphen = false;

            foreach (var ch in trimmed)
            {
                if (char.IsLetterOrDigit(ch))
                {
                    builder.Append(ch);
                    lastWasHyphen = false;
                }
                else if (char.IsWhiteSpace(ch) || ch == '-' || ch == '_')
                {
                    if (!lastWasHyphen && builder.Length > 0)
                    {
                        builder.Append('-');
                        lastWasHyphen = true;
                    }
                }
            }

            return builder.ToString().Trim('-');
        }
    }
}
