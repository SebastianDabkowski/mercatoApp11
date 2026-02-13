using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.Modules.Products.Domain.Interfaces;

namespace SD.ProjectName.Modules.Products.Application
{
    public class GetProducts
    {
        private readonly IProductRepository _repository;

        public GetProducts(IProductRepository repository)
        {
            _repository = repository;
        }

        public async Task<List<ProductModel>> GetList(string? sellerId = null, bool includeDrafts = false)
        {
            return await _repository.GetList(sellerId, includeDrafts);
        }

        public async Task<ProductModel?> GetById(int id, bool includeDrafts = false)
        {
            return await _repository.GetById(id, includeDrafts);
        }

        public async Task<List<ProductModel>> GetFilteredList(string sellerId, bool includeDrafts, string? search, string? workflowState, CancellationToken cancellationToken = default)
        {
            return await _repository.GetListFiltered(sellerId, includeDrafts, search, workflowState, cancellationToken);
        }

        public async Task<List<ProductModel>> GetByCategoryIds(IEnumerable<int> categoryIds, bool includeDrafts = false)
        {
            return await _repository.GetByCategoryIds(categoryIds, includeDrafts);
        }

        public async Task<List<ProductModel>> SearchActive(string search, CancellationToken cancellationToken = default)
        {
            return await _repository.SearchActiveProducts(search, cancellationToken);
        }

        public async Task<List<ProductModel>> SearchActiveLimited(string search, int limit, CancellationToken cancellationToken = default)
        {
            return await _repository.SearchActiveProductsLimited(search, limit, cancellationToken);
        }

        public async Task<List<ProductModel>> FilterActive(ProductFilterOptions filters, CancellationToken cancellationToken = default)
        {
            return await _repository.FilterActiveProducts(filters, cancellationToken);
        }

        public async Task<PagedResult<ProductModel>> FilterActivePaged(ProductFilterOptions filters, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
        {
            return await _repository.FilterActiveProductsPaged(filters, pageNumber, pageSize, cancellationToken);
        }

        public async Task<ProductFilterMetadata> GetFilterMetadata(ProductFilterContext context, CancellationToken cancellationToken = default)
        {
            return await _repository.GetFilterMetadata(context, cancellationToken);
        }

    }
}
