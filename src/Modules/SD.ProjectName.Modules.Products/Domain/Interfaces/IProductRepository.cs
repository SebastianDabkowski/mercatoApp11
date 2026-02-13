using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SD.ProjectName.Modules.Products.Domain.Interfaces
{
    public interface IProductRepository
    {
        Task<List<ProductModel>> GetList(string? sellerId = null, bool includeDrafts = false);

        Task<ProductModel?> GetById(int id, bool includeDrafts = false);

        Task<List<ProductModel>> GetByIds(IEnumerable<int> ids, bool includeDrafts = false);

        Task<List<ProductModel>> GetByCategoryIds(IEnumerable<int> categoryIds, bool includeDrafts = false);

        Task Add(ProductModel product);

        Task Update(ProductModel product);

        Task<ProductModel?> GetBySku(string sellerId, string merchantSku, bool includeDrafts = false);

        Task<List<ProductModel>> GetListFiltered(string sellerId, bool includeDrafts, string? search = null, string? workflowState = null, CancellationToken cancellationToken = default);

        Task<List<ProductModel>> SearchActiveProducts(string search, CancellationToken cancellationToken = default);
        Task<List<ProductModel>> SearchActiveProductsLimited(string search, int limit, CancellationToken cancellationToken = default);

        Task<List<ProductModel>> FilterActiveProducts(ProductFilterOptions filters, CancellationToken cancellationToken = default);

        Task<PagedResult<ProductModel>> FilterActiveProductsPaged(ProductFilterOptions filters, int pageNumber, int pageSize, CancellationToken cancellationToken = default);

        Task<ProductFilterMetadata> GetFilterMetadata(ProductFilterContext context, CancellationToken cancellationToken = default);
    }
}
