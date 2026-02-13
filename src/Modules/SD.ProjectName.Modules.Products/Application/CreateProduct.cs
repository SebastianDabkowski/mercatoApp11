using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.Modules.Products.Domain.Interfaces;

namespace SD.ProjectName.Modules.Products.Application
{
    public class CreateProduct
    {
        private readonly IProductRepository _repository;

        public CreateProduct(IProductRepository repository)
        {
            _repository = repository;
        }

        public async Task<ProductModel> CreateAsync(ProductModel product)
        {
            if (string.IsNullOrWhiteSpace(product.WorkflowState))
            {
                product.WorkflowState = ProductWorkflowStates.Draft;
            }

            product.ModerationStatus = ProductModerationStatuses.Normalize(product.ModerationStatus);
            product.ModerationNote = string.IsNullOrWhiteSpace(product.ModerationNote) ? null : product.ModerationNote.Trim();
            product.Condition = ProductConditions.Normalize(product.Condition);

            await _repository.Add(product);
            return product;
        }
    }
}
