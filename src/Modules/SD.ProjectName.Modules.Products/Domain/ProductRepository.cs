using Microsoft.EntityFrameworkCore;
using SD.ProjectName.Modules.Products.Domain.Interfaces;
using SD.ProjectName.Modules.Products.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SD.ProjectName.Modules.Products.Domain
{

    public class ProductRepository : IProductRepository
    {
        private readonly ProductDbContext _context;

        public ProductRepository(ProductDbContext context)
        {
            _context = context;
        }

        public async Task<List<ProductModel>> GetList(string? sellerId = null, bool includeDrafts = false)
        {
            var query = _context.Set<ProductModel>()
                .Where(p => p.WorkflowState != ProductWorkflowStates.Archived)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(sellerId))
            {
                query = query.Where(p => p.SellerId == sellerId);
            }

            if (!includeDrafts)
            {
                query = query.Where(p => p.WorkflowState == ProductWorkflowStates.Active);
            }

            return await query
                .OrderByDescending(p => p.Id)
                .ToListAsync();
        }

        public async Task<List<ProductModel>> GetListFiltered(string sellerId, bool includeDrafts, string? search = null, string? workflowState = null, CancellationToken cancellationToken = default)
        {
            var query = _context.Set<ProductModel>()
                .Where(p => p.WorkflowState != ProductWorkflowStates.Archived && p.SellerId == sellerId)
                .AsQueryable();

            if (!includeDrafts)
            {
                query = query.Where(p => p.WorkflowState == ProductWorkflowStates.Active);
            }

            if (!string.IsNullOrWhiteSpace(workflowState))
            {
                query = query.Where(p => p.WorkflowState == workflowState);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(p => p.Title.Contains(term) || p.MerchantSku.Contains(term));
            }

            return await query
                .OrderByDescending(p => p.Id)
                .ToListAsync(cancellationToken);
        }

        public async Task<List<ProductModel>> GetByIds(IEnumerable<int> ids, bool includeDrafts = false)
        {
            if (ids == null || !ids.Any())
            {
                return new List<ProductModel>();
            }

            var query = _context.Set<ProductModel>()
                .Where(p => p.WorkflowState != ProductWorkflowStates.Archived)
                .AsQueryable();

            if (!includeDrafts)
            {
                query = query.Where(p => p.WorkflowState == ProductWorkflowStates.Active);
            }

            var idList = ids.Distinct().ToList();
            return await query
                .Where(p => idList.Contains(p.Id))
                .ToListAsync();
        }

        public async Task<List<ProductModel>> GetByCategoryIds(IEnumerable<int> categoryIds, bool includeDrafts = false)
        {
            if (categoryIds == null)
            {
                return new List<ProductModel>();
            }

            var ids = categoryIds.Distinct().ToList();
            if (ids.Count == 0)
            {
                return new List<ProductModel>();
            }

            var query = _context.Set<ProductModel>()
                .Where(p => p.WorkflowState != ProductWorkflowStates.Archived)
                .Where(p => p.CategoryId != null && ids.Contains(p.CategoryId.Value))
                .AsQueryable();

            if (!includeDrafts)
            {
                query = query.Where(p => p.WorkflowState == ProductWorkflowStates.Active);
            }

            return await query
                .OrderByDescending(p => p.Id)
                .ToListAsync();
        }

        public async Task Add(ProductModel product)
        {
            _context.Set<ProductModel>().Add(product);
            await _context.SaveChangesAsync();
        }

        public async Task<ProductModel?> GetById(int id, bool includeDrafts = false)
        {
            var query = _context.Set<ProductModel>()
                .Where(p => p.WorkflowState != ProductWorkflowStates.Archived)
                .AsQueryable();

            if (!includeDrafts)
            {
                query = query.Where(p => p.WorkflowState == ProductWorkflowStates.Active);
            }

            return await query.FirstOrDefaultAsync(p => p.Id == id);
        }

        public async Task Update(ProductModel product)
        {
            _context.Set<ProductModel>().Update(product);
            await _context.SaveChangesAsync();
        }

        public async Task<ProductModel?> GetBySku(string sellerId, string merchantSku, bool includeDrafts = false)
        {
            if (string.IsNullOrWhiteSpace(sellerId) || string.IsNullOrWhiteSpace(merchantSku))
            {
                return null;
            }

            var query = _context.Set<ProductModel>()
                .Where(p => p.SellerId == sellerId && p.MerchantSku == merchantSku && p.WorkflowState != ProductWorkflowStates.Archived);

            if (!includeDrafts)
            {
                query = query.Where(p => p.WorkflowState == ProductWorkflowStates.Active);
            }

            return await query.FirstOrDefaultAsync();
        }
    }
}
