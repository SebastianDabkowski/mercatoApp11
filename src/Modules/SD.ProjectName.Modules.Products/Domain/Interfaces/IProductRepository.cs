using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SD.ProjectName.Modules.Products.Domain.Interfaces
{
    public interface IProductRepository
    {
        Task<List<ProductModel>> GetList(string? sellerId = null, bool includeDrafts = false);

        Task<ProductModel?> GetById(int id, bool includeDrafts = false);

        Task Add(ProductModel product);

        Task Update(ProductModel product);
    }
}
