using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SD.ProjectName.Modules.Products.Application;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages.Products
{
    public class SearchModel : PageModel
    {
        private readonly GetProducts _getProducts;
        private readonly ManageCategories _manageCategories;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IAnalyticsTracker? _analyticsTracker;
        private const int MaxQueryLength = 200;
        private const int DefaultPageSize = 12;

        public SearchModel(GetProducts getProducts, ManageCategories manageCategories, UserManager<ApplicationUser> userManager, IAnalyticsTracker? analyticsTracker = null)
        {
            _getProducts = getProducts;
            _manageCategories = manageCategories;
            _userManager = userManager;
            _analyticsTracker = analyticsTracker;
        }

        [BindProperty(SupportsGet = true)]
        public string? Q { get; set; }

        [BindProperty(SupportsGet = true)]
        public int? CategoryId { get; set; }

        [BindProperty(SupportsGet = true)]
        public decimal? MinPrice { get; set; }

        [BindProperty(SupportsGet = true)]
        public decimal? MaxPrice { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Condition { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? SellerId { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Sort { get; set; }

        [BindProperty(SupportsGet = true, Name = "page")]
        public int PageNumber { get; set; } = 1;

        public List<ProductModel> Results { get; private set; } = new();

        public string? StatusMessage { get; private set; }

        public int TotalResults { get; private set; }

        public int TotalPages { get; private set; }

        public int PageSize => DefaultPageSize;

        public List<SelectListItem> CategoryOptions { get; private set; } = new();

        public List<SelectListItem> SellerOptions { get; private set; } = new();

        public List<string> ConditionOptions { get; private set; } = ProductConditions.Allowed.ToList();

        public ProductFilterMetadata FilterMetadata { get; private set; } = new();

        public List<string> ActiveFilters { get; private set; } = new();

        public List<SelectListItem> SortOptions { get; private set; } = new();

        public string AppliedSort { get; private set; } = ProductSortOptions.Relevance;

        public bool HasActiveFilters => ActiveFilters.Any();

        public async Task OnGetAsync(CancellationToken cancellationToken)
        {
            await LoadCategoryOptions(cancellationToken);

            if (string.IsNullOrWhiteSpace(Q))
            {
                StatusMessage = "Enter a keyword to search active products by title or description.";
                return;
            }

            NormalizePriceBounds();
            var term = NormalizeQuery(Q);

            Q = term;
            AppliedSort = ProductSortOptions.Normalize(Sort, hasSearch: true);
            Sort = AppliedSort;
            SortOptions = BuildSortOptions(AppliedSort);

            var categoryIds = CategoryId.HasValue ? new[] { CategoryId.Value } : Array.Empty<int>();
            var context = new ProductFilterContext
            {
                Search = term,
                CategoryIds = categoryIds
            };

            FilterMetadata = await _getProducts.GetFilterMetadata(context, cancellationToken);
            ConditionOptions = FilterMetadata.Conditions.Any() ? FilterMetadata.Conditions : ConditionOptions;

            var sellerNames = await LoadSellerNames(FilterMetadata.SellerIds, cancellationToken);
            SellerOptions = sellerNames
                .Select(s => new SelectListItem { Value = s.Key, Text = s.Value, Selected = SellerId == s.Key })
                .OrderBy(s => s.Text)
                .ToList();

            var filterOptions = new ProductFilterOptions
            {
                Search = term,
                CategoryIds = categoryIds,
                MinPrice = MinPrice,
                MaxPrice = MaxPrice,
                Condition = NormalizeCondition(),
                SellerId = string.IsNullOrWhiteSpace(SellerId) ? null : SellerId,
                SortBy = AppliedSort
            };

            PageNumber = Math.Max(PageNumber, 1);
            var pagedResults = await _getProducts.FilterActivePaged(filterOptions, PageNumber, DefaultPageSize, cancellationToken);
            TotalResults = pagedResults.TotalCount;
            TotalPages = pagedResults.TotalPages;
            PageNumber = pagedResults.PageNumber;
            Results = pagedResults.Items;
            BuildActiveFilters(filterOptions, sellerNames);

            if (!Results.Any())
            {
                StatusMessage = filterOptions.HasAnyFilters()
                    ? "No products match these filters. Clear filters to see all results for this search."
                    : "No products match this search. Try adjusting your keywords or browse categories.";
            }

            if (_analyticsTracker != null)
            {
                await _analyticsTracker.TrackAsync(
                    new AnalyticsEventEntry(
                        AnalyticsEventTypes.Search,
                        Keyword: term,
                        SellerId: filterOptions.SellerId,
                        Metadata: new Dictionary<string, string?>
                        {
                            ["categoryId"] = CategoryId?.ToString(),
                            ["hasFilters"] = filterOptions.HasAnyFilters().ToString(),
                            ["results"] = TotalResults.ToString()
                        }),
                    cancellationToken);
            }
        }

        private string NormalizeQuery(string query)
        {
            var term = query.Trim();
            if (term.Length > MaxQueryLength)
            {
                term = term[..MaxQueryLength];
            }

            return term;
        }

        private void NormalizePriceBounds()
        {
            if (MinPrice.HasValue && MinPrice < 0)
            {
                MinPrice = 0;
            }

            if (MaxPrice.HasValue && MaxPrice < 0)
            {
                MaxPrice = 0;
            }

            if (MinPrice.HasValue && MaxPrice.HasValue && MinPrice > MaxPrice)
            {
                (MinPrice, MaxPrice) = (MaxPrice, MinPrice);
            }
        }

        private string? NormalizeCondition()
        {
            if (string.IsNullOrWhiteSpace(Condition))
            {
                return null;
            }

            return ProductConditions.IsValid(Condition) ? ProductConditions.Normalize(Condition) : null;
        }

        private async Task LoadCategoryOptions(CancellationToken cancellationToken)
        {
            var tree = await _manageCategories.GetTree();
            CategoryOptions = tree
                .Select(c => new SelectListItem
                {
                    Value = c.Id.ToString(),
                    Text = c.FullPath,
                    Selected = CategoryId.HasValue && CategoryId.Value == c.Id
                })
                .ToList();
        }

        private async Task<Dictionary<string, string>> LoadSellerNames(IEnumerable<string> sellerIds, CancellationToken cancellationToken)
        {
            var ids = sellerIds.Distinct().Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
            if (!ids.Any())
            {
                return new Dictionary<string, string>();
            }

            var sellers = await _userManager.Users
                .Where(u => ids.Contains(u.Id))
                .Select(u => new { u.Id, u.BusinessName, u.Email })
                .ToListAsync(cancellationToken);

            return sellers.ToDictionary(
                s => s.Id,
                s => string.IsNullOrWhiteSpace(s.BusinessName) ? (s.Email ?? s.Id) : s.BusinessName!);
        }

        private void BuildActiveFilters(ProductFilterOptions filters, IReadOnlyDictionary<string, string> sellerNames)
        {
            var active = new List<string>();

            if (CategoryId.HasValue)
            {
                var selected = CategoryOptions.FirstOrDefault(c => c.Value == CategoryId.Value.ToString());
                if (selected != null)
                {
                    active.Add($"Category: {selected.Text}");
                }
            }

            if (filters.MinPrice.HasValue)
            {
                active.Add($"Min price {filters.MinPrice.Value:C}");
            }

            if (filters.MaxPrice.HasValue)
            {
                active.Add($"Max price {filters.MaxPrice.Value:C}");
            }

            if (!string.IsNullOrWhiteSpace(filters.Condition))
            {
                active.Add($"Condition: {filters.Condition}");
            }

            if (!string.IsNullOrWhiteSpace(filters.SellerId))
            {
                if (!sellerNames.TryGetValue(filters.SellerId, out var sellerName))
                {
                    sellerName = "Selected seller";
                }

                active.Add($"Seller: {sellerName}");
            }

            ActiveFilters = active;
        }

        private static List<SelectListItem> BuildSortOptions(string selected)
        {
            return new List<SelectListItem>
            {
                new() { Value = ProductSortOptions.Relevance, Text = "Relevance", Selected = selected == ProductSortOptions.Relevance },
                new() { Value = ProductSortOptions.PriceAsc, Text = "Price: Low to High", Selected = selected == ProductSortOptions.PriceAsc },
                new() { Value = ProductSortOptions.PriceDesc, Text = "Price: High to Low", Selected = selected == ProductSortOptions.PriceDesc },
                new() { Value = ProductSortOptions.Newest, Text = "Newest", Selected = selected == ProductSortOptions.Newest }
            };
        }
    }
}
