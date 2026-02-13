using Microsoft.EntityFrameworkCore;
using SD.ProjectName.Modules.Products.Domain.Interfaces;
using SD.ProjectName.Modules.Products.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
            var query = _context.Set<ProductModel>().AsQueryable();

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

        public async Task Add(ProductModel product)
        {
            _context.Set<ProductModel>().Add(product);
            await _context.SaveChangesAsync();
        }
    }
}
