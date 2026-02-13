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

        public async Task<List<ProductModel>> SearchActiveProducts(string search, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(search))
            {
                return new List<ProductModel>();
            }

            return await FilterActiveProducts(new ProductFilterOptions { Search = search }, cancellationToken);
        }

        public async Task<List<ProductModel>> SearchActiveProductsLimited(string search, int limit, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(search) || limit <= 0)
            {
                return new List<ProductModel>();
            }

            var normalizedLimit = Math.Max(1, Math.Min(limit, 20));
            var query = BuildFilteredQuery(
                new ProductFilterOptions { Search = search, SortBy = ProductSortOptions.Relevance },
                out _,
                out _);

            return await query
                .Take(normalizedLimit)
                .ToListAsync(cancellationToken);
        }

        public async Task<List<ProductModel>> FilterActiveProducts(ProductFilterOptions filters, CancellationToken cancellationToken = default)
        {
            var query = BuildFilteredQuery(filters, out _, out _);
            return await query.ToListAsync(cancellationToken);
        }

        public async Task<PagedResult<ProductModel>> FilterActiveProductsPaged(ProductFilterOptions filters, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
        {
            var query = BuildFilteredQuery(filters, out _, out _);
            var total = await query.CountAsync(cancellationToken);
            var normalizedPageSize = Math.Max(1, Math.Min(pageSize, 100));
            var totalPages = normalizedPageSize > 0 ? (int)Math.Ceiling(total / (double)normalizedPageSize) : 0;
            var normalizedPageNumber = totalPages > 0 ? Math.Min(Math.Max(pageNumber, 1), totalPages) : 1;
            var skip = (normalizedPageNumber - 1) * normalizedPageSize;

            var items = await query
                .Skip(skip)
                .Take(normalizedPageSize)
                .ToListAsync(cancellationToken);

            return new PagedResult<ProductModel>
            {
                Items = items,
                TotalCount = total,
                PageNumber = normalizedPageNumber,
                PageSize = normalizedPageSize
            };
        }

        public async Task<ProductFilterMetadata> GetFilterMetadata(ProductFilterContext context, CancellationToken cancellationToken = default)
        {
            var query = BuildActiveQuery(context, out _);
            var metadata = new ProductFilterMetadata();

            if (!await query.AnyAsync(cancellationToken))
            {
                return metadata;
            }

            metadata.MinPrice = await query.MinAsync(p => p.Price, cancellationToken);
            metadata.MaxPrice = await query.MaxAsync(p => p.Price, cancellationToken);
            metadata.Conditions = await query
                .Select(p => string.IsNullOrWhiteSpace(p.Condition) ? ProductConditions.New : p.Condition)
                .Distinct()
                .ToListAsync(cancellationToken);
            metadata.SellerIds = await query
                .Select(p => p.SellerId)
                .Distinct()
                .ToListAsync(cancellationToken);

            return metadata;
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

        private IQueryable<ProductModel> BuildFilteredQuery(ProductFilterOptions filters, out bool hasSearch, out string? normalizedSearchValue)
        {
            var query = BuildActiveQuery(filters, out var normalizedSearch);
            normalizedSearchValue = normalizedSearch;
            var searchValue = normalizedSearchValue;

            if (filters.MinPrice.HasValue)
            {
                query = query.Where(p => p.Price >= filters.MinPrice.Value);
            }

            if (filters.MaxPrice.HasValue)
            {
                query = query.Where(p => p.Price <= filters.MaxPrice.Value);
            }

            if (!string.IsNullOrWhiteSpace(filters.Condition) && ProductConditions.IsValid(filters.Condition))
            {
                var normalizedCondition = ProductConditions.Normalize(filters.Condition);
                query = query.Where(p =>
                    p.Condition == normalizedCondition ||
                    (string.IsNullOrEmpty(p.Condition) && normalizedCondition == ProductConditions.New));
            }

            if (!string.IsNullOrWhiteSpace(filters.SellerId))
            {
                query = query.Where(p => p.SellerId == filters.SellerId);
            }

            hasSearch = !string.IsNullOrWhiteSpace(searchValue);
            var sort = ProductSortOptions.Normalize(filters.SortBy, hasSearch);

            query = sort switch
            {
                ProductSortOptions.PriceAsc => query.OrderBy(p => p.Price).ThenByDescending(p => p.Id),
                ProductSortOptions.PriceDesc => query.OrderByDescending(p => p.Price).ThenByDescending(p => p.Id),
                ProductSortOptions.Relevance when hasSearch && searchValue != null => query
                    .OrderByDescending(p => p.Title.ToLower().Contains(searchValue))
                    .ThenByDescending(p => !string.IsNullOrWhiteSpace(p.Description) && p.Description!.ToLower().Contains(searchValue))
                    .ThenByDescending(p => p.Id),
                _ => query.OrderByDescending(p => p.Id)
            };

            return query;
        }

        private IQueryable<ProductModel> BuildActiveQuery(ProductFilterContext context, out string? normalizedSearch)
        {
            string? normalizedSearchValue = null;
            var query = _context.Set<ProductModel>()
                .Where(p => p.WorkflowState == ProductWorkflowStates.Active);

            if (!string.IsNullOrWhiteSpace(context.Search))
            {
                var term = NormalizeSearch(context.Search!);
                normalizedSearchValue = term.ToLowerInvariant();
                var searchTerm = normalizedSearchValue;
                query = query.Where(p =>
                    p.Title.ToLower().Contains(searchTerm) ||
                    (!string.IsNullOrWhiteSpace(p.Description) && p.Description!.ToLower().Contains(searchTerm)));
            }

            if (context.CategoryIds != null)
            {
                var ids = context.CategoryIds.Where(id => id > 0).Distinct().ToList();
                if (ids.Count > 0)
                {
                    query = query.Where(p => p.CategoryId != null && ids.Contains(p.CategoryId.Value));
                }
            }

            normalizedSearch = normalizedSearchValue;
            return query;
        }

        private static string NormalizeSearch(string search)
        {
            var term = search.Trim();
            if (term.Length > 200)
            {
                term = term[..200];
            }

            return term;
        }
    }
}
