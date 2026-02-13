using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SD.ProjectName.Modules.Products.Application;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.WebApp.Identity;

namespace SD.ProjectName.WebApp.Pages.Products
{
    public class CategoryModel : PageModel
    {
        private readonly ManageCategories _manageCategories;
        private readonly GetProducts _getProducts;
        private readonly UserManager<ApplicationUser> _userManager;

        public CategoryModel(ManageCategories manageCategories, GetProducts getProducts, UserManager<ApplicationUser> userManager)
        {
            _manageCategories = manageCategories;
            _getProducts = getProducts;
            _userManager = userManager;
        }

        public CategoryNode? CurrentCategory { get; private set; }

        public List<CategoryNode> Subcategories { get; private set; } = new();

        public List<CategoryNode> Breadcrumb { get; private set; } = new();

        public List<ProductModel> Products { get; private set; } = new();

        [BindProperty(SupportsGet = true)]
        public bool IncludeSubcategories { get; set; } = true;

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

        public List<SelectListItem> CategoryOptions { get; private set; } = new();

        public List<SelectListItem> SellerOptions { get; private set; } = new();

        public List<string> ConditionOptions { get; private set; } = ProductConditions.Allowed.ToList();

        public ProductFilterMetadata FilterMetadata { get; private set; } = new();

        public List<string> ActiveFilters { get; private set; } = new();

        public List<SelectListItem> SortOptions { get; private set; } = new();

        public string AppliedSort { get; private set; } = ProductSortOptions.Newest;

        public bool HasActiveFilters => ActiveFilters.Any();

        public string? StatusMessage { get; private set; }

        public async Task<IActionResult> OnGetAsync(int? id, CancellationToken cancellationToken)
        {
            CategoryId = id ?? CategoryId;
            var categories = await _manageCategories.GetTree();
            if (!categories.Any())
            {
                return Page();
            }

            CurrentCategory = CategoryId.HasValue
                ? categories.FirstOrDefault(c => c.Id == CategoryId.Value)
                : categories.FirstOrDefault(c => c.ParentId == null);

            if (CurrentCategory == null)
            {
                Response.StatusCode = StatusCodes.Status404NotFound;
                return NotFound();
            }

            NormalizePriceBounds();
            CategoryOptions = categories
                .Select(c => new SelectListItem
                {
                    Value = c.Id.ToString(),
                    Text = c.FullPath,
                    Selected = c.Id == CurrentCategory.Id
                })
                .ToList();

            Breadcrumb = BuildBreadcrumb(CurrentCategory, categories);
            Subcategories = categories
                .Where(c => c.ParentId == CurrentCategory.Id)
                .OrderBy(c => c.SortOrder)
                .ThenBy(c => c.Name)
                .ToList();

            var targetIds = CollectCategoryIds(CurrentCategory.Id, categories, IncludeSubcategories);
            var filterContext = new ProductFilterContext { CategoryIds = targetIds };
            FilterMetadata = await _getProducts.GetFilterMetadata(filterContext, cancellationToken);
            ConditionOptions = FilterMetadata.Conditions.Any() ? FilterMetadata.Conditions : ConditionOptions;
            AppliedSort = ProductSortOptions.Normalize(Sort, hasSearch: false);
            Sort = AppliedSort;
            SortOptions = BuildSortOptions(AppliedSort);

            var sellerNames = await LoadSellerNames(FilterMetadata.SellerIds, cancellationToken);
            SellerOptions = sellerNames
                .Select(s => new SelectListItem { Value = s.Key, Text = s.Value, Selected = SellerId == s.Key })
                .OrderBy(s => s.Text)
                .ToList();

            var filters = new ProductFilterOptions
            {
                CategoryIds = targetIds,
                MinPrice = MinPrice,
                MaxPrice = MaxPrice,
                Condition = NormalizeCondition(),
                SellerId = string.IsNullOrWhiteSpace(SellerId) ? null : SellerId,
                SortBy = AppliedSort
            };

            Products = await _getProducts.FilterActive(filters, cancellationToken);
            BuildActiveFilters(filters, sellerNames);

            if (!Products.Any())
            {
                StatusMessage = filters.HasAnyFilters()
                    ? "No products match these filters. Clear filters to see all products in this category."
                    : "No products found in this category.";
            }

            return Page();
        }

        private static List<CategoryNode> BuildBreadcrumb(CategoryNode current, IReadOnlyList<CategoryNode> all)
        {
            var lookup = all.ToDictionary(c => c.Id, c => c);
            var path = new List<CategoryNode> { current };
            var cursor = current;

            while (cursor.ParentId.HasValue && lookup.TryGetValue(cursor.ParentId.Value, out var parent))
            {
                path.Add(parent);
                cursor = parent;
            }

            path.Reverse();
            return path;
        }

        private static List<int> CollectCategoryIds(int rootId, IReadOnlyList<CategoryNode> all, bool includeChildren)
        {
            var ids = new List<int> { rootId };
            if (!includeChildren)
            {
                return ids;
            }

            AddChildren(rootId, ids, all);
            return ids;
        }

        private static void AddChildren(int parentId, List<int> ids, IReadOnlyList<CategoryNode> all)
        {
            var children = all.Where(c => c.ParentId == parentId).ToList();
            foreach (var child in children)
            {
                ids.Add(child.Id);
                AddChildren(child.Id, ids, all);
            }
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

            var selected = CategoryOptions.FirstOrDefault(c => c.Selected);
            if (selected != null)
            {
                active.Add($"Category: {selected.Text}");
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
                new() { Value = ProductSortOptions.Newest, Text = "Newest", Selected = selected == ProductSortOptions.Newest },
                new() { Value = ProductSortOptions.PriceAsc, Text = "Price: Low to High", Selected = selected == ProductSortOptions.PriceAsc },
                new() { Value = ProductSortOptions.PriceDesc, Text = "Price: High to Low", Selected = selected == ProductSortOptions.PriceDesc },
                new() { Value = ProductSortOptions.Relevance, Text = "Relevance", Selected = selected == ProductSortOptions.Relevance }
            };
        }
    }
}
