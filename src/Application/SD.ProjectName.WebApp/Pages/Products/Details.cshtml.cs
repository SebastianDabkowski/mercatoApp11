using System;
using System.Collections.Generic;
using System.Linq;
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
        private readonly OrderService _orderService;
        private readonly ILogger<DetailsModel> _logger;
        private readonly IAnalyticsTracker? _analyticsTracker;

        public DetailsModel(GetProducts getProducts, RecentlyViewedService recentlyViewed, UserManager<ApplicationUser> userManager, OrderService orderService, ILogger<DetailsModel> logger, IAnalyticsTracker? analyticsTracker = null)
        {
            _getProducts = getProducts;
            _recentlyViewed = recentlyViewed;
            _userManager = userManager;
            _orderService = orderService;
            _logger = logger;
            _analyticsTracker = analyticsTracker;
        }

        public ProductModel? Product { get; private set; }
        public IReadOnlyList<ProductModel> RecentlyViewedProducts { get; private set; } = Array.Empty<ProductModel>();
        public string? ReturnUrl { get; private set; }
        public string? SellerDisplayName { get; private set; }
        public string? SellerSlug { get; private set; }
        public bool HasSellerLink => !string.IsNullOrWhiteSpace(SellerSlug) && !string.IsNullOrWhiteSpace(SellerDisplayName);
        public IReadOnlyList<ProductReviewView> Reviews { get; private set; } = Array.Empty<ProductReviewView>();
        public double? AverageRating { get; private set; }
        public int PageNumber { get; private set; } = 1;
        public int TotalPages { get; private set; } = 1;
        public string SortOption { get; private set; } = "newest";
        public IReadOnlyList<string> ReportReasons => ReviewReportReasons.Allowed;
        public IReadOnlyList<ProductQuestionView> Questions { get; private set; } = Array.Empty<ProductQuestionView>();
        private const int ReviewsPageSize = 10;

        [TempData]
        public string? StatusMessage { get; set; }

        [BindProperty]
        public string? QuestionContent { get; set; }

        public async Task<IActionResult> OnGetAsync(int id, string? returnUrl = null, string? sort = null, int page = 1)
        {
            ReturnUrl = GetSafeReturnUrl(returnUrl);
            SortOption = NormalizeSortOption(sort);
            PageNumber = page <= 0 ? 1 : page;
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
            var reviewsPage = await _orderService.GetPublishedReviewsPageAsync(Product.Id, PageNumber, ReviewsPageSize, SortOption, HttpContext.RequestAborted);
            Reviews = reviewsPage.Reviews;
            AverageRating = reviewsPage.AverageRating;
            SortOption = reviewsPage.Sort;
            PageNumber = reviewsPage.PageNumber;
            TotalPages = Math.Max(1, (int)Math.Ceiling(reviewsPage.TotalCount / (double)reviewsPage.PageSize));
            Questions = await _orderService.GetProductQuestionsAsync(Product.Id, HttpContext.RequestAborted);

            if (_analyticsTracker != null)
            {
                await _analyticsTracker.TrackAsync(
                    new AnalyticsEventEntry(
                        AnalyticsEventTypes.ProductView,
                        ProductId: Product.Id,
                        SellerId: Product.SellerId),
                    HttpContext.RequestAborted);
            }

            return Page();
        }

        public async Task<IActionResult> OnPostQuestionAsync(int id, string? returnUrl = null)
        {
            ReturnUrl = GetSafeReturnUrl(returnUrl);
            Product = await _getProducts.GetById(id);
            if (Product == null)
            {
                Response.StatusCode = StatusCodes.Status404NotFound;
                StatusMessage = "This product is unavailable.";
                RecentlyViewedProducts = await _recentlyViewed.GetProductsAsync(HttpContext, id);
                return Page();
            }

            await LoadSellerMetadata(Product.SellerId);
            RecentlyViewedProducts = await _recentlyViewed.GetProductsAsync(HttpContext, Product.Id);
            Questions = await _orderService.GetProductQuestionsAsync(Product.Id, HttpContext.RequestAborted);

            if (User?.Identity?.IsAuthenticated != true)
            {
                ModelState.AddModelError(string.Empty, "Sign in as a buyer to ask a question.");
                return Page();
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null || !string.Equals(user.AccountType, AccountTypes.Buyer, StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError(string.Empty, "Only buyers can submit questions.");
                return Page();
            }

            if (string.IsNullOrWhiteSpace(QuestionContent))
            {
                ModelState.AddModelError(nameof(QuestionContent), "Enter your question.");
                return Page();
            }

            var sellerId = Product.SellerId;
            if (string.IsNullOrWhiteSpace(sellerId))
            {
                ModelState.AddModelError(string.Empty, "Seller information is unavailable.");
                return Page();
            }

            var result = await _orderService.SubmitProductQuestionAsync(
                Product.Id,
                sellerId,
                user.Id,
                user.FullName ?? user.UserName ?? user.Email ?? "Buyer",
                QuestionContent!,
                HttpContext.RequestAborted);

            if (!result.Success)
            {
                ModelState.AddModelError(string.Empty, result.Error ?? "Unable to submit your question.");
                return Page();
            }

            StatusMessage = "Question sent to the seller.";
            return RedirectToPage(new { id, returnUrl = ReturnUrl });
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

        private static string NormalizeSortOption(string? sort)
        {
            return sort?.Trim().ToLowerInvariant() switch
            {
                "highest" or "rating_desc" => "highest",
                "lowest" or "rating_asc" => "lowest",
                _ => "newest"
            };
        }
    }
}
