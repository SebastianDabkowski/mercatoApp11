using Microsoft.EntityFrameworkCore;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.Modules.Products.Infrastructure;

namespace SD.ProjectName.Modules.Products.Application
{
    public record CategoryOperationResult(bool Success, string? Error = null)
    {
        public static CategoryOperationResult Ok() => new(true, null);

        public static CategoryOperationResult Failed(string error) => new(false, error);
    }

    public record CategoryNode(int Id, string Name, string FullPath, int? ParentId, int SortOrder, bool IsActive, int Depth, int ProductCount);

    public class ManageCategories
    {
        private readonly ProductDbContext _context;

        public ManageCategories(ProductDbContext context)
        {
            _context = context;
        }

        public async Task<IReadOnlyList<CategoryNode>> GetTree(bool includeInactive = false)
        {
            await EnsureRootCategoryAsync();

            var categories = await _context.Categories
                .Where(c => includeInactive || c.IsActive)
                .OrderBy(c => c.ParentId)
                .ThenBy(c => c.SortOrder)
                .ThenBy(c => c.Name)
                .ToListAsync();

            var productCounts = await _context.Products
                .Where(p => p.CategoryId != null)
                .GroupBy(p => p.CategoryId!.Value)
                .Select(g => new { CategoryId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.CategoryId, g => g.Count);

            return BuildTree(categories, productCounts);
        }

        public async Task<List<CategoryModel>> GetActiveCategories()
        {
            await EnsureRootCategoryAsync();

            return await _context.Categories
                .Where(c => c.IsActive)
                .OrderBy(c => c.ParentId)
                .ThenBy(c => c.SortOrder)
                .ThenBy(c => c.Name)
                .ToListAsync();
        }

        public async Task<CategoryModel?> GetById(int id, bool includeInactive = false)
        {
            var query = _context.Categories.AsQueryable();

            if (!includeInactive)
            {
                query = query.Where(c => c.IsActive);
            }

            return await query.FirstOrDefaultAsync(c => c.Id == id);
        }

        private async Task EnsureRootCategoryAsync()
        {
            if (await _context.Categories.AnyAsync())
            {
                return;
            }

            _context.Categories.Add(new CategoryModel
            {
                Name = "General",
                FullPath = "General",
                SortOrder = 0,
                IsActive = true
            });

            await _context.SaveChangesAsync();
        }

        public async Task<(CategoryOperationResult Result, CategoryModel? Category)> CreateAsync(string name, int? parentId)
        {
            var trimmed = name?.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return (CategoryOperationResult.Failed("Name is required."), null);
            }

            CategoryModel? parent = null;
            if (parentId.HasValue)
            {
                parent = await _context.Categories.FirstOrDefaultAsync(c => c.Id == parentId.Value);
                if (parent == null)
                {
                    return (CategoryOperationResult.Failed("Parent category not found."), null);
                }
            }

            var siblingNames = await _context.Categories
                .Where(c => c.ParentId == parentId)
                .Select(c => c.Name)
                .ToListAsync();

            if (siblingNames.Any(n => n.Equals(trimmed, StringComparison.OrdinalIgnoreCase)))
            {
                return (CategoryOperationResult.Failed("A category with this name already exists under the selected parent."), null);
            }

            var nextSort = await _context.Categories
                .Where(c => c.ParentId == parentId)
                .Select(c => (int?)c.SortOrder)
                .MaxAsync() ?? -1;

            var category = new CategoryModel
            {
                Name = trimmed,
                ParentId = parentId,
                SortOrder = nextSort + 1,
                FullPath = BuildFullPath(trimmed, parent?.FullPath ?? string.Empty),
                IsActive = true
            };

            _context.Categories.Add(category);
            await _context.SaveChangesAsync();

            return (CategoryOperationResult.Ok(), category);
        }

        public async Task<CategoryOperationResult> RenameAsync(int id, string newName)
        {
            var trimmed = newName?.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return CategoryOperationResult.Failed("Name is required.");
            }

            var categories = await _context.Categories.ToListAsync();
            var category = categories.FirstOrDefault(c => c.Id == id);
            if (category == null)
            {
                return CategoryOperationResult.Failed("Category not found.");
            }

            if (categories.Any(c => c.ParentId == category.ParentId && c.Id != id && c.Name.Equals(trimmed, StringComparison.OrdinalIgnoreCase)))
            {
                return CategoryOperationResult.Failed("Another category with this name already exists at the same level.");
            }

            category.Name = trimmed;
            var parentPath = categories.FirstOrDefault(c => c.Id == category.ParentId)?.FullPath ?? string.Empty;
            UpdateFullPathRecursive(category, categories, parentPath);

            var affectedIds = CollectBranchIds(category, categories);
            await UpdateProductLabelsForCategories(categories, affectedIds);
            await _context.SaveChangesAsync();

            return CategoryOperationResult.Ok();
        }

        public async Task<CategoryOperationResult> UpdateSortOrderAsync(int id, int sortOrder)
        {
            var category = await _context.Categories.FirstOrDefaultAsync(c => c.Id == id);
            if (category == null)
            {
                return CategoryOperationResult.Failed("Category not found.");
            }

            category.SortOrder = sortOrder;
            await _context.SaveChangesAsync();
            return CategoryOperationResult.Ok();
        }

        public async Task<CategoryOperationResult> SetActiveAsync(int id, bool isActive)
        {
            var category = await _context.Categories.FirstOrDefaultAsync(c => c.Id == id);
            if (category == null)
            {
                return CategoryOperationResult.Failed("Category not found.");
            }

            category.IsActive = isActive;
            await _context.SaveChangesAsync();
            return CategoryOperationResult.Ok();
        }

        public async Task<CategoryOperationResult> DeleteAsync(int id)
        {
            var categories = await _context.Categories.ToListAsync();
            var category = categories.FirstOrDefault(c => c.Id == id);
            if (category == null)
            {
                return CategoryOperationResult.Failed("Category not found.");
            }

            var branchIds = CollectBranchIds(category, categories);
            if (branchIds.Count > 1)
            {
                return CategoryOperationResult.Failed("Remove or re-parent child categories before deleting this category.");
            }

            var hasProducts = await _context.Products.AnyAsync(p => p.CategoryId == id);
            if (hasProducts)
            {
                return CategoryOperationResult.Failed("Cannot delete a category that has products assigned. Reassign products first.");
            }

            _context.Categories.Remove(category);
            await _context.SaveChangesAsync();

            return CategoryOperationResult.Ok();
        }

        private static List<int> CollectBranchIds(CategoryModel category, List<CategoryModel> all)
        {
            var ids = new List<int> { category.Id };
            var children = all.Where(c => c.ParentId == category.Id).ToList();
            foreach (var child in children)
            {
                ids.AddRange(CollectBranchIds(child, all));
            }

            return ids;
        }

        private static string BuildFullPath(string name, string parentPath)
        {
            if (string.IsNullOrWhiteSpace(parentPath))
            {
                return name;
            }

            return $"{parentPath} / {name}";
        }

        private void UpdateFullPathRecursive(CategoryModel category, List<CategoryModel> all, string parentPath)
        {
            category.FullPath = BuildFullPath(category.Name, parentPath);

            var children = all.Where(c => c.ParentId == category.Id).ToList();
            foreach (var child in children)
            {
                UpdateFullPathRecursive(child, all, category.FullPath);
            }
        }

        private async Task UpdateProductLabelsForCategories(List<CategoryModel> allCategories, List<int> categoryIds)
        {
            var products = await _context.Products
                .Where(p => p.CategoryId != null && categoryIds.Contains(p.CategoryId.Value))
                .ToListAsync();

            var lookup = allCategories.ToDictionary(c => c.Id, c => c.FullPath);
            foreach (var product in products)
            {
                if (product.CategoryId.HasValue && lookup.TryGetValue(product.CategoryId.Value, out var path))
                {
                    product.Category = path;
                }
            }
        }

        private static List<CategoryNode> BuildTree(List<CategoryModel> categories, Dictionary<int, int> productCounts)
        {
            var result = new List<CategoryNode>();
            var ordered = categories
                .OrderBy(c => c.SortOrder)
                .ThenBy(c => c.Name)
                .ToList();

            AddChildren(null, 0);

            return result;

            void AddChildren(int? parentId, int depth)
            {
                foreach (var category in ordered.Where(c => c.ParentId == parentId))
                {
                    productCounts.TryGetValue(category.Id, out var count);
                    result.Add(new CategoryNode(category.Id, category.Name, category.FullPath, category.ParentId, category.SortOrder, category.IsActive, depth, count));
                    AddChildren(category.Id, depth + 1);
                }
            }
        }
    }
}
