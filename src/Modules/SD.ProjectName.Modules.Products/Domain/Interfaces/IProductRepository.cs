using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SD.ProjectName.Modules.Products.Domain.Interfaces
{
    public interface IProductRepository
    {
        Task<List<ProductModel>> GetList();
    }
}
