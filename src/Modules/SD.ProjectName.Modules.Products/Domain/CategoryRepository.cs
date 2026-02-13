using Microsoft.EntityFrameworkCore;
using SD.ProjectName.Modules.Products.Domain.Interfaces;
using SD.ProjectName.Modules.Products.Infrastructure;

namespace SD.ProjectName.Modules.Products.Domain
{
    public class CategoryRepository : ICategoryRepository
    {
        private readonly ProductDbContext _context;

        public CategoryRepository(ProductDbContext context)
        {
            _context = context;
        }

        public async Task<List<CategoryModel>> GetAll(bool includeInactive = false)
        {
            var query = _context.Set<CategoryModel>().AsQueryable();

            if (!includeInactive)
            {
                query = query.Where(c => c.IsActive);
            }

            return await query
                .OrderBy(c => c.ParentId)
                .ThenBy(c => c.SortOrder)
                .ThenBy(c => c.Name)
                .ToListAsync();
        }

        public async Task<CategoryModel?> GetById(int id, bool includeInactive = false)
        {
            var query = _context.Set<CategoryModel>().AsQueryable();

            if (!includeInactive)
            {
                query = query.Where(c => c.IsActive);
            }

            return await query.FirstOrDefaultAsync(c => c.Id == id);
        }

        public async Task Add(CategoryModel category)
        {
            _context.Set<CategoryModel>().Add(category);
            await _context.SaveChangesAsync();
        }

        public async Task Update(CategoryModel category)
        {
            _context.Set<CategoryModel>().Update(category);
            await _context.SaveChangesAsync();
        }

        public async Task Delete(CategoryModel category)
        {
            _context.Set<CategoryModel>().Remove(category);
            await _context.SaveChangesAsync();
        }

        public async Task<int> GetProductCount(int categoryId)
        {
            return await _context.Products.CountAsync(p => p.CategoryId == categoryId);
        }
    }
}
