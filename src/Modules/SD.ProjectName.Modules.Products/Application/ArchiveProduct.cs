using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.Modules.Products.Domain.Interfaces;

namespace SD.ProjectName.Modules.Products.Application
{
    public class ArchiveProduct
    {
        private readonly IProductRepository _repository;

        public ArchiveProduct(IProductRepository repository)
        {
            _repository = repository;
        }

        public async Task ArchiveAsync(ProductModel product)
        {
            product.WorkflowState = ProductWorkflowStates.Archived;
            await _repository.Update(product);
        }
    }
}
