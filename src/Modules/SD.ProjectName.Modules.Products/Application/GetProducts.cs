using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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

        public async Task<List<ProductModel>> GetList()
        {
            return await _repository.GetList();
        }

    }
}
