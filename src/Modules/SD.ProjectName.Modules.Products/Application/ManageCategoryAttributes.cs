using Microsoft.EntityFrameworkCore;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.Modules.Products.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SD.ProjectName.Modules.Products.Application
{
    public class ManageCategoryAttributes
    {
        private readonly ProductDbContext _context;

        public ManageCategoryAttributes(ProductDbContext context)
        {
            _context = context;
        }

        public async Task<List<CategoryAttributeDefinition>> GetForCategoryAsync(int categoryId, bool includeDeprecated = false)
        {
            var query = _context.CategoryAttributeDefinitions
                .Include(d => d.Usages)
                .Where(d => d.Usages.Any(u => u.CategoryId == categoryId));

            if (!includeDeprecated)
            {
                query = query.Where(d => !d.IsDeprecated);
            }

            return await query.OrderBy(d => d.Name).ToListAsync();
        }

        public async Task<Dictionary<int, List<CategoryAttributeDefinition>>> GetForCategoriesAsync(IEnumerable<int> categoryIds, bool includeDeprecated = false)
        {
            var idSet = categoryIds.ToHashSet();

            var query = _context.CategoryAttributeDefinitions
                .Include(d => d.Usages)
                .Where(d => d.Usages.Any(u => idSet.Contains(u.CategoryId)));

            if (!includeDeprecated)
            {
                query = query.Where(d => !d.IsDeprecated);
            }

            var definitions = await query.ToListAsync();
            return definitions
                .SelectMany(def => def.Usages.Where(u => idSet.Contains(u.CategoryId)).Select(u => new { u.CategoryId, Definition = def }))
                .GroupBy(x => x.CategoryId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Definition).Distinct().OrderBy(d => d.Name).ToList());
        }

        public async Task<List<CategoryAttributeDefinition>> GetLinkableDefinitionsAsync(int categoryId)
        {
            return await _context.CategoryAttributeDefinitions
                .Where(d => !d.IsDeprecated)
                .Where(d => !_context.CategoryAttributeUsages.Any(u => u.CategoryId == categoryId && u.DefinitionId == d.Id))
                .OrderBy(d => d.Name)
                .ToListAsync();
        }

        public async Task<(bool Success, string? Error, CategoryAttributeDefinition? Definition)> AddOrLinkAsync(int categoryId, string name, string type, bool isRequired, string? options)
        {
            var trimmed = name?.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return (false, "Name is required.", null);
            }

            var normalizedType = CategoryAttributeTypes.Normalize(type);
            var normalizedOptions = NormalizeOptions(options, normalizedType);

            var existing = await _context.CategoryAttributeDefinitions
                .Include(d => d.Usages)
                .FirstOrDefaultAsync(d =>
                    d.Name == trimmed &&
                    d.Type == normalizedType &&
                    (d.Options ?? string.Empty) == (normalizedOptions ?? string.Empty));

            if (existing != null)
            {
                existing.IsDeprecated = false;
                existing.IsRequired = isRequired;
                if (!existing.Usages.Any(u => u.CategoryId == categoryId))
                {
                    existing.Usages.Add(new CategoryAttributeUsage { CategoryId = categoryId, DefinitionId = existing.Id });
                }

                await _context.SaveChangesAsync();
                return (true, null, existing);
            }

            var definition = new CategoryAttributeDefinition
            {
                Name = trimmed,
                Type = normalizedType,
                IsRequired = isRequired,
                Options = normalizedOptions
            };

            _context.CategoryAttributeDefinitions.Add(definition);
            _context.CategoryAttributeUsages.Add(new CategoryAttributeUsage
            {
                CategoryId = categoryId,
                Definition = definition
            });

            await _context.SaveChangesAsync();
            return (true, null, definition);
        }

        public async Task<(bool Success, string? Error)> UpdateDefinitionAsync(int definitionId, string name, string type, bool isRequired, string? options)
        {
            var trimmed = name?.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return (false, "Name is required.");
            }

            var normalizedType = CategoryAttributeTypes.Normalize(type);
            var normalizedOptions = NormalizeOptions(options, normalizedType);

            var definition = await _context.CategoryAttributeDefinitions.FirstOrDefaultAsync(d => d.Id == definitionId);
            if (definition == null)
            {
                return (false, "Attribute not found.");
            }

            var conflict = await _context.CategoryAttributeDefinitions.AnyAsync(d =>
                d.Id != definitionId &&
                d.Name == trimmed &&
                d.Type == normalizedType &&
                (d.Options ?? string.Empty) == (normalizedOptions ?? string.Empty));

            if (conflict)
            {
                return (false, "Another attribute with the same name and type already exists.");
            }

            definition.Name = trimmed;
            definition.Type = normalizedType;
            definition.IsRequired = isRequired;
            definition.Options = normalizedOptions;

            await _context.SaveChangesAsync();
            return (true, null);
        }

        public async Task<(bool Success, string? Error)> LinkExistingAsync(int definitionId, int categoryId)
        {
            var definition = await _context.CategoryAttributeDefinitions.FirstOrDefaultAsync(d => d.Id == definitionId);
            if (definition == null)
            {
                return (false, "Attribute not found.");
            }

            var alreadyLinked = await _context.CategoryAttributeUsages.AnyAsync(u => u.CategoryId == categoryId && u.DefinitionId == definitionId);
            if (alreadyLinked)
            {
                return (true, null);
            }

            _context.CategoryAttributeUsages.Add(new CategoryAttributeUsage
            {
                CategoryId = categoryId,
                DefinitionId = definitionId
            });

            await _context.SaveChangesAsync();
            return (true, null);
        }

        public async Task<(bool Success, string? Error)> SetDeprecatedAsync(int definitionId, bool isDeprecated)
        {
            var definition = await _context.CategoryAttributeDefinitions.FirstOrDefaultAsync(d => d.Id == definitionId);
            if (definition == null)
            {
                return (false, "Attribute not found.");
            }

            definition.IsDeprecated = isDeprecated;
            await _context.SaveChangesAsync();
            return (true, null);
        }

        private static string? NormalizeOptions(string? options, string type)
        {
            if (type != CategoryAttributeTypes.List)
            {
                return null;
            }

            var values = options?
                .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();

            return values.Any() ? string.Join(",", values) : null;
        }
    }
}
