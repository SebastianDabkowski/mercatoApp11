using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.Modules.Products.Domain.Interfaces;

namespace SD.ProjectName.Modules.Products.Application
{
    public class UpdateProduct
    {
        private readonly IProductRepository _repository;

        public UpdateProduct(IProductRepository repository)
        {
            _repository = repository;
        }

        public async Task<ProductModel> UpdateAsync(ProductModel product)
        {
            if (string.IsNullOrWhiteSpace(product.WorkflowState))
            {
                product.WorkflowState = ProductWorkflowStates.Draft;
            }

            product.ModerationStatus = ProductModerationStatuses.Normalize(product.ModerationStatus);
            product.ModerationNote = string.IsNullOrWhiteSpace(product.ModerationNote) ? null : product.ModerationNote.Trim();
            product.Condition = ProductConditions.Normalize(product.Condition);

            await _repository.Update(product);
            return product;
        }
    }
}
