using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.Modules.Products.Application;
using SD.ProjectName.Modules.Products.Domain;

namespace SD.ProjectName.WebApp.Pages.Products
{
    public class CategoryModel : PageModel
    {
        private readonly ManageCategories _manageCategories;
        private readonly GetProducts _getProducts;

        public CategoryModel(ManageCategories manageCategories, GetProducts getProducts)
        {
            _manageCategories = manageCategories;
            _getProducts = getProducts;
        }

        public CategoryNode? CurrentCategory { get; private set; }

        public List<CategoryNode> Subcategories { get; private set; } = new();

        public List<CategoryNode> Breadcrumb { get; private set; } = new();

        public List<ProductModel> Products { get; private set; } = new();

        [BindProperty(SupportsGet = true)]
        public bool IncludeSubcategories { get; set; } = true;

        public async Task<IActionResult> OnGetAsync(int? id)
        {
            var categories = await _manageCategories.GetTree();
            if (!categories.Any())
            {
                return Page();
            }

            CurrentCategory = id.HasValue
                ? categories.FirstOrDefault(c => c.Id == id.Value)
                : categories.FirstOrDefault(c => c.ParentId == null);

            if (CurrentCategory == null)
            {
                Response.StatusCode = StatusCodes.Status404NotFound;
                return NotFound();
            }

            Breadcrumb = BuildBreadcrumb(CurrentCategory, categories);
            Subcategories = categories
                .Where(c => c.ParentId == CurrentCategory.Id)
                .OrderBy(c => c.SortOrder)
                .ThenBy(c => c.Name)
                .ToList();

            var targetIds = CollectCategoryIds(CurrentCategory.Id, categories, IncludeSubcategories);
            Products = await _getProducts.GetByCategoryIds(targetIds);

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
    }
}
